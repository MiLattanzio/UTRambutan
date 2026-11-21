#pragma once
#include <QDateTime>
#include <QNetworkCookie>
#include <QUrl>

inline bool musicCookie(const QNetworkCookie &cookie)
{
    auto domain = cookie.domain().toLower();
    if (domain.startsWith('.')) domain.remove(0, 1);
    return (domain == "youtube.com" || domain == "music.youtube.com") &&
        cookie.path() == "/" && !cookie.name().isEmpty() && cookie.name().size() < 128 &&
        cookie.value().size() < 8192 &&
        (cookie.isSessionCookie() || cookie.expirationDate() > QDateTime::currentDateTimeUtc());
}
inline bool allowedNavigation(const QUrl &url)
{
    const auto host = url.host().toLower();
    return url.scheme() == "https" && url.userInfo().isEmpty() &&
        (host == "youtube.com" || host.endsWith(".youtube.com") || host == "google.com" || host.endsWith(".google.com"));
}
