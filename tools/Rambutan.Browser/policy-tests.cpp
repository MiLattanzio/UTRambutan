#include "CookiePolicy.h"
#include "BrowserState.h"
#include <cstdio>

int main()
{
    QNetworkCookie cookie("SAPISID", "synthetic-only");
    cookie.setDomain(".youtube.com"); cookie.setPath("/");
    if (!musicCookie(cookie)) return 1;
    for (const auto &domain : {"accounts.google.com", ".youtube.com.attacker.invalid", "notyoutube.com", ".google.com"}) {
        cookie.setDomain(domain); if (musicCookie(cookie)) return 2;
    }
    cookie.setDomain("music.youtube.com"); if (!musicCookie(cookie)) return 3;
    cookie.setExpirationDate(QDateTime::currentDateTimeUtc().addSecs(-1)); if (musicCookie(cookie)) return 4;
    if (!allowedNavigation(QUrl("https://accounts.google.com/signin"))) return 5;
    for (const auto &url : {"http://music.youtube.com", "https://youtube.com.attacker.invalid", "file:///etc/passwd", "https://evil@music.youtube.com"})
        if (allowedNavigation(QUrl(url))) return 6;
    const QRectF view(0, 321, 1224, 2379);
    if (keyboardBottomInset(QRectF(0, 1668, 1224, 1032), view, 1224, 3) != 344) return 7;
    if (keyboardBottomInset(QRectF(0, 556, 408, 344), view, 1224, 3) != 344) return 8;
    if (keyboardBottomInset(QRectF(0, 1668, 1224, 1032), QRectF(0, 321, 1224, 1347), 1224, 3) != 0) return 9;
    if (keyboardBottomInset(QRectF(), view, 1224, 3) != 0) return 10;
    if (keyboardBottomInset(QRectF(0, 1668, 1224, 1032), QRectF(0, 600, 1224, 1800), 1224, 3) != 244) return 11;
    CookieImportSchedule retry;
    if (retry.offer("first", 0) || retry.offer("first", 749)) return 12;
    if (!retry.offer("first", 750) || retry.offer("first", 800, true)) return 13;
    retry.retry(1000);
    if (retry.offer("first", 2999) || !retry.offer("first", 3000)) return 14;
    retry.retry(3100);
    if (retry.offer("first", 7000) || !retry.offer("first", 7100)) return 15;
    retry.retry(7200);
    if (retry.offer("changed", 7201) || !retry.offer("changed", 7951)) return 16;
    retry.retry(8000);
    if (!retry.offer("changed", 8001, true)) return 17;
    // Fairphone 5: the dialog extends past the usable surface. Its keyboard
    // ends at 2595 native pixels, before the reported viewport bottom of 2698.
    if (keyboardBottomInset(QRectF(0, 1567, 1224, 1028), QRectF(12, 217, 1212, 2481), 1224, 3) != 377) return 18;
    std::puts("Browser cookie policy, keyboard geometry and import retries: passed");
    return 0;
}
