#include "CookiePolicy.h"
#include "BrowserState.h"
#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QQuickWebEngineProfile>
#include <QWebEngineCookieStore>
#include <QtWebEngineQuick/qtwebenginequickglobal.h>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QMap>
#include <QSocketNotifier>
#include <QTimer>
#include <QWindow>
#include <QInputMethod>
#include <QScreen>
#include <QElapsedTimer>
#include <QCryptographicHash>
#ifdef BROWSER_INPUT_TEST
#include <QFile>
#endif
#include <cstdio>
#include <csignal>
#include <memory>
#include <QLocale>
#include <unistd.h>
#include <sys/prctl.h>
// Include after Qt headers: Xlib defines several common names as macros.
#include <X11/Xlib.h>
#undef None
#undef Bool
#undef Status

class LoginBridge : public QObject
{
    Q_OBJECT
    Q_PROPERTY(QString status READ status NOTIFY statusChanged)
    Q_PROPERTY(QString host READ host NOTIFY hostChanged)
    Q_PROPERTY(int keyboardInset READ keyboardInset NOTIFY keyboardInsetChanged)
    Q_PROPERTY(QUrl initialUrl READ initialUrl CONSTANT)
public:
    explicit LoginBridge(QObject *parent = nullptr) : QObject(parent)
    {
        elapsed.start();
        timer.setInterval(1000);
        connect(&timer, &QTimer::timeout, this, [this] { offer(false); });
        timer.start();
        auto input = new QSocketNotifier(STDIN_FILENO, QSocketNotifier::Read, this);
        connect(input, &QSocketNotifier::activated, this, [this] {
            char chunk[256]; const auto n = ::read(STDIN_FILENO, chunk, sizeof(chunk));
            if (n <= 0) { QCoreApplication::quit(); return; }
            commands.append(chunk, n);
            if (commands.size() > 1024) { QCoreApplication::exit(3); return; }
            while (commands.contains('\n')) {
                const auto end = commands.indexOf('\n'); const auto command = commands.left(end).trimmed();
                commands.remove(0, end + 1);
                if (command == "accepted") { QCoreApplication::quit(); return; }
                if (command == "cancel") { QCoreApplication::exit(1); return; }
                checking = false;
                schedule.retry(elapsed.elapsed());
                message = command == "offline" ? tr("Connection unavailable. Retrying automatically; you can also tap Finish.")
                    : tr("Waiting for YouTube Music to finish signing in. Retrying automatically…");
                emit statusChanged();
            }
        });
    }
    QString status() const { return message; }
    QString host() const { return current.host(); }
    QUrl initialUrl() const {
#ifdef BROWSER_INPUT_TEST
        return QUrl("qrc:/InputTest.html");
#else
        return QUrl("https://music.youtube.com/");
#endif
    }
    int keyboardInset() const { return inset; }
    void setKeyboardInset(int value) {
        if (value == inset) return;
        inset = value; emit keyboardInsetChanged();
    }
    Q_INVOKABLE bool allow(const QUrl &url) const {
#ifdef BROWSER_INPUT_TEST
        return url == initialUrl();
#else
        return allowedNavigation(url);
#endif
    }
    Q_INVOKABLE void page(const QUrl &url) { current = url; emit hostChanged(); }
    Q_INVOKABLE void check() {
        emit refreshCookies();
        QTimer::singleShot(200, this, [this] {
            if (!hasAuthentication() && current != initialUrl()) emit openMusic();
            offer(true);
        });
    }
    Q_INVOKABLE void loaded() { emit refreshCookies(); }
    Q_INVOKABLE void cancel() { QCoreApplication::exit(1); }
    void added(const QNetworkCookie &cookie)
    {
        const auto key = cookie.domain() + '\n' + cookie.path() + '\n' + cookie.name();
        if (musicCookie(cookie)) cookies.insert(key, cookie); else cookies.remove(key);
    }
    void removed(const QNetworkCookie &cookie)
    { cookies.remove(cookie.domain() + '\n' + cookie.path() + '\n' + cookie.name()); }
private:
    bool hasAuthentication() const {
        for (const auto &cookie : cookies)
            if (musicCookie(cookie) && (cookie.name() == "SAPISID" || cookie.name() == "__Secure-3PAPISID")) return true;
        return false;
    }
    void offer(bool explicitCheck)
    {
        // Cookie origin and server verification determine validity. Requiring
        // the final Music URL misses successful sign-ins ending on www.youtube.
        if (checking) return;
        QJsonArray values;
        bool authentication = false;
        for (const auto &cookie : cookies) {
            if (!musicCookie(cookie)) continue;
            if (cookie.name() == "SAPISID" || cookie.name() == "__Secure-3PAPISID") authentication = true;
            values.append(QJsonObject{{"name", QString::fromUtf8(cookie.name())}, {"value", QString::fromUtf8(cookie.value())},
                {"domain", cookie.domain()}, {"path", cookie.path()},
                {"expires", cookie.isSessionCookie() ? 0 : cookie.expirationDate().toSecsSinceEpoch()}});
        }
        if (!authentication || values.size() > 64) return;
        const auto json = QJsonDocument(QJsonObject{{"cookies", values}}).toJson(QJsonDocument::Compact);
        if (json.size() > 65536 || !schedule.offer(QCryptographicHash::hash(json, QCryptographicHash::Sha256), elapsed.elapsed(), explicitCheck)) return;
        checking = true;
        message = tr("Checking your YouTube Music session…"); emit statusChanged();
        // The inherited anonymous pipe is the only credential transport. Never stderr or a file.
        std::fwrite(json.constData(), 1, json.size(), stdout); std::fputc('\n', stdout); std::fflush(stdout);
    }
    QMap<QString, QNetworkCookie> cookies;
    QUrl current;
    QByteArray commands;
    CookieImportSchedule schedule;
    QElapsedTimer elapsed;
    QTimer timer;
    int inset = 0;
    bool checking = false;
    QString message = tr("Sign in to YouTube Music. Import is automatic; tap Finish if the browser stays open.");
signals:
    void statusChanged();
    void hostChanged();
    void keyboardInsetChanged();
    void refreshCookies();
    void openMusic();
};

int main(int argc, char **argv)
{
    // Exit if Rambutan crashes or closes. Session cookies live only in this process.
    const auto parent = getppid();
    prctl(PR_SET_PDEATHSIG, SIGTERM);
    if (getppid() != parent) return 1;
    // Lomiri's launcher selects the Qt5-only Suru style. The helper uses Qt6.
    qputenv("QT_QUICK_CONTROLS_STYLE", "Basic");
    // Embed an X11 child in the AXAML dialog's compositor surface. A separate
    // top-level session in this Click cgroup confuses Maliit's PID focus check.
    if (!qEnvironmentVariableIsEmpty("DISPLAY")) {
        qputenv("QT_QPA_PLATFORM", "xcb");
        if (!qEnvironmentVariableIsSet("QT_SCALE_FACTOR")) {
            bool ok = false;
            const double grid = qEnvironmentVariable("GRID_UNIT_PX").toDouble(&ok);
            if (ok && grid >= 8 && grid <= 64)
                qputenv("QT_SCALE_FACTOR", QByteArray::number(grid / 8));
        }
    }
    QCoreApplication::setApplicationName("rambutan.milattanzio");
    QGuiApplication::setDesktopFileName(qEnvironmentVariable("APP_ID", "rambutan.milattanzio_rambutan"));
    QtWebEngineQuick::initialize();
    QGuiApplication app(argc, argv);
    app.setQuitOnLastWindowClosed(true);
    LoginBridge bridge;
    QQuickWebEngineProfile profile;
    profile.setOffTheRecord(true);
    profile.setHttpCacheType(QQuickWebEngineProfile::MemoryHttpCache);
    profile.setPersistentCookiesPolicy(QQuickWebEngineProfile::NoPersistentCookies);
    profile.setHttpAcceptLanguage(QLocale::system().name().replace('_', '-'));
    QObject::connect(profile.cookieStore(), &QWebEngineCookieStore::cookieAdded, &bridge, &LoginBridge::added);
    QObject::connect(profile.cookieStore(), &QWebEngineCookieStore::cookieRemoved, &bridge, &LoginBridge::removed);
    QObject::connect(&bridge, &LoginBridge::refreshCookies, profile.cookieStore(), &QWebEngineCookieStore::loadAllCookies);
    QQmlApplicationEngine engine;
    engine.rootContext()->setContextProperty("login", &bridge);
    engine.rootContext()->setContextProperty("loginProfile", &profile);
    engine.load(QUrl("qrc:/Browser.qml"));
    if (engine.rootObjects().isEmpty()) return 3;
    auto window = qobject_cast<QWindow *>(engine.rootObjects().first());
    if (!window) return 3;
    bool embedded = false;
    const auto parentId = qEnvironmentVariable("RAMBUTAN_BROWSER_PARENT_XID").toULongLong(&embedded);
    std::unique_ptr<QWindow> host;
    Display *display = nullptr;
    QTimer geometry;
    if (embedded && parentId && app.platformName() == "xcb") {
        host.reset(QWindow::fromWinId(static_cast<WId>(parentId)));
        display = XOpenDisplay(nullptr);
        if (!host || !display) return 3;
        window->setFlags(Qt::SubWindow);
        window->setParent(host.get());
        auto resize = [&, window] {
            XWindowAttributes attributes;
            if (XGetWindowAttributes(display, static_cast<::Window>(parentId), &attributes)) {
                const auto scale = window->devicePixelRatio();
                int x = 0, y = 0; ::Window child;
                XTranslateCoordinates(display, static_cast<::Window>(parentId), DefaultRootWindow(display), 0, 0, &x, &y, &child);
                const auto screen = window->screen()->geometry();
                // Avalonia's embedded dialog can extend beyond the screen. Use
                // its visible client area, otherwise the keyboard misses the
                // bottom edge and controls can remain underneath it.
                const int width = qMax(1, qMin(attributes.width, qFloor((screen.x() + screen.width()) * scale) - x));
                const int height = qMax(1, qMin(attributes.height, qFloor((screen.y() + screen.height()) * scale) - y));
                const QRect desired(0, 0, qMax(1, qFloor(width / scale)), qMax(1, qFloor(height / scale)));
                if (window->geometry() != desired) window->setGeometry(desired);
                bridge.setKeyboardInset(keyboardBottomInset(app.inputMethod()->keyboardRectangle(),
                    QRectF(x, y, desired.width() * scale, desired.height() * scale),
                    window->screen()->geometry().width() * scale, scale));
#ifdef BROWSER_INPUT_TEST
                const auto rect = app.inputMethod()->keyboardRectangle();
                const auto info = QString("scale=%1 screen=%2,%3,%4,%5 parent=%6,%7,%8,%9 child=%10,%11 keyboard=%12,%13,%14,%15 inset=%16\n")
                    .arg(scale).arg(screen.x()).arg(screen.y()).arg(screen.width()).arg(screen.height())
                    .arg(x).arg(y).arg(attributes.width).arg(attributes.height).arg(desired.width()).arg(desired.height())
                    .arg(rect.x()).arg(rect.y()).arg(rect.width()).arg(rect.height()).arg(bridge.keyboardInset()).toUtf8();
                static QByteArray previous;
                if (previous != info) {
                    previous = info;
                    QFile diagnostic(qEnvironmentVariable("TMPDIR") + "/rambutan-input-geometry.log");
                    if (diagnostic.open(QIODevice::WriteOnly | QIODevice::Append)) diagnostic.write(info);
                }
#endif
            }
        };
        QObject::connect(&geometry, &QTimer::timeout, window, resize);
        QObject::connect(app.inputMethod(), &QInputMethod::keyboardRectangleChanged, window, resize);
        geometry.start(100);
        resize();
        window->show();
        window->requestActivate();
    } else {
        window->showMaximized();
    }
    QTimer::singleShot(15 * 60 * 1000, &app, [] { QCoreApplication::exit(2); });
    const auto result = app.exec();
    // Detach before deleting the foreign-parent wrapper. Qt owns only our child.
    geometry.stop();
    window->hide();
    window->setParent(nullptr);
    if (display) XCloseDisplay(display);
    return result;
}
#include "main.moc"
