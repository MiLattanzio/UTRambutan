#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QWindow>
#include <QTimer>
#include <QFile>
#include <QFileInfo>
#include <QDir>
#include <QJsonDocument>
#include <QJsonObject>
#include <QSocketNotifier>
#include <QUrl>
#include <X11/Xlib.h>
#undef Bool
#include <sys/prctl.h>
#include <unistd.h>
#include <csignal>
#include <cstdio>
#include <memory>

class PickerBridge : public QObject {
    Q_OBJECT
public:
    Q_INVOKABLE bool collect(const QUrl &url) {
        const QDir destination(qEnvironmentVariable("RAMBUTAN_PICKER_DIRECTORY"));
        if (!url.isLocalFile() || !destination.isAbsolute() || !destination.exists()) return false;
        const QFileInfo source(url.toLocalFile());
        if (!source.isFile() || source.fileName().isEmpty()) return false;
        const auto path = destination.filePath(source.fileName());
        if (!QFile::copy(source.absoluteFilePath(), path)) return false;
        QFile::setPermissions(path, QFileDevice::ReadOwner | QFileDevice::WriteOwner);
        m_path = path;
        return true;
    }
    Q_INVOKABLE void finish(bool saved) {
        if (saved) {
            const auto json = QJsonDocument(QJsonObject{{"path", m_path}}).toJson(QJsonDocument::Compact);
            std::fwrite(json.constData(), 1, json.size(), stdout);
            std::fputc('\n', stdout);
            std::fflush(stdout);
        }
        QCoreApplication::exit(saved ? 0 : 3);
    }
    Q_INVOKABLE void cancel() { QCoreApplication::exit(0); }
private:
    QString m_path;
};

int main(int argc, char **argv) {
    const auto parent = getppid();
    prctl(PR_SET_PDEATHSIG, SIGTERM);
    if (getppid() != parent) return 1;
    qputenv("QT_QPA_PLATFORM", "xcb");
    if (!qEnvironmentVariableIsSet("QT_SCALE_FACTOR")) {
        bool valid = false;
        const auto grid = qEnvironmentVariable("GRID_UNIT_PX").toDouble(&valid);
        if (valid && grid >= 8 && grid <= 64) qputenv("QT_SCALE_FACTOR", QByteArray::number(grid / 8));
    }
    QCoreApplication::setApplicationName("rambutan.milattanzio");
    QGuiApplication::setDesktopFileName(qEnvironmentVariable("APP_ID"));
    QCoreApplication::setAttribute(Qt::AA_EnableHighDpiScaling);
    QGuiApplication app(argc, argv);
    PickerBridge bridge;
    QQmlApplicationEngine engine;
    engine.rootContext()->setContextProperty("pickerBridge", &bridge);
    engine.load(QUrl("qrc:/FilePicker.qml"));
    if (engine.rootObjects().isEmpty()) return 3;
    auto window = qobject_cast<QWindow *>(engine.rootObjects().first());
    if (!window) return 3;
    QSocketNotifier commands(STDIN_FILENO, QSocketNotifier::Read);
    QObject::connect(&commands, &QSocketNotifier::activated, &app, [&] {
        char input[64];
        if (::read(STDIN_FILENO, input, sizeof(input)) <= 0) commands.setEnabled(false);
        QMetaObject::invokeMethod(window, "cancel");
    });
    bool valid = false;
    const auto parentId = qEnvironmentVariable("RAMBUTAN_PICKER_PARENT_XID").toULongLong(&valid);
    if (!valid || !parentId) return 3;
    std::unique_ptr<QWindow> host(QWindow::fromWinId(static_cast<WId>(parentId)));
    Display *display = XOpenDisplay(nullptr);
    if (!host || !display) return 3;
    window->setFlags(Qt::SubWindow);
    window->setParent(host.get());
    QTimer geometry;
    auto resize = [&] {
        XWindowAttributes attributes;
        if (XGetWindowAttributes(display, static_cast<::Window>(parentId), &attributes)) {
            const auto scale = window->devicePixelRatio();
            const QRect rect(0, 0, qMax(1, qRound(attributes.width / scale)), qMax(1, qRound(attributes.height / scale)));
            if (window->geometry() != rect) window->setGeometry(rect);
        }
    };
    QObject::connect(&geometry, &QTimer::timeout, window, resize);
    geometry.start(100);
    resize();
    window->show();
    window->requestActivate();
    QTimer::singleShot(15 * 60 * 1000, &app, [] { QCoreApplication::exit(2); });
    const auto result = app.exec();
    geometry.stop();
    window->hide();
    window->setParent(nullptr);
    XCloseDisplay(display);
    return result;
}
#include "file-picker.moc"
