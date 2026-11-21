#pragma once
#include <QByteArray>
#include <QRectF>
#include <QtGlobal>
#include <QtMath>
#include <cmath>

// Maliit on UT reports a global rectangle. Older Qt6 plugins return native
// pixels; newer plugins scale that rectangle to device-independent units.
inline int keyboardBottomInset(QRectF keyboard, const QRectF &viewportPixels,
                               double screenWidthPixels, double scale)
{
    if (keyboard.isEmpty() || viewportPixels.isEmpty() || !std::isfinite(scale) || scale <= 0) return 0;
    if (scale > 1 && keyboard.width() <= screenWidthPixels / scale + 2)
        keyboard = QRectF(keyboard.x() * scale, keyboard.y() * scale,
                          keyboard.width() * scale, keyboard.height() * scale);
    const auto overlap = keyboard.intersected(viewportPixels);
    if (overlap.isEmpty()) return 0;
    // Lomiri's XWayland surface can extend below the usable screen. The
    // docked keyboard rectangle stops at that usable boundary, so reserve
    // everything below its top, including any off-screen part of the child.
    return qCeil((viewportPixels.bottom() - overlap.top()) / scale);
}

class CookieImportSchedule
{
public:
    bool offer(const QByteArray &candidate, qint64 now, bool force = false)
    {
        if (inFlight) return false;
        if (candidate != fingerprint) {
            fingerprint = candidate;
            attempts = 0;
            nextAttempt = now + 750; // let redirects finish setting the cookie set
        }
        if (!force && now < nextAttempt) return false;
        inFlight = true;
        ++attempts;
        return true;
    }
    void retry(qint64 now)
    {
        inFlight = false;
        nextAttempt = now + qMin<qint64>(30000, 2000LL << qBound(0, attempts - 1, 4));
    }
private:
    QByteArray fingerprint;
    qint64 nextAttempt = 0;
    int attempts = 0;
    bool inFlight = false;
};
