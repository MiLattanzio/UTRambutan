import QtQuick 2.15
import QtQuick.Window 2.15
import QtQuick.Controls 2.15
import QtWebEngine 1.10

ApplicationWindow {
    id: window
    visible: false
    width: 408
    height: 840
    title: "Rambutan — YouTube Music"
    color: "#16161c"
    palette.window: "#16161c"
    palette.windowText: "white"
    palette.button: "#282830"
    palette.buttonText: "white"
    palette.highlight: "#f04452"
    Connections {
        target: loginProfile
        function onDownloadRequested(download) { download.cancel() }
    }
    Connections {
        target: login
        function onOpenMusic() { web.url = login.initialUrl }
    }
    // Explicit widths avoid the wrapped Label / ColumnLayout implicit-size
    // feedback loop in Qt 5, which can leave no room for the web page.
    header: Item {
        id: browserHeader
        height: 48 + (statusLabel.visible ? statusLabel.implicitHeight + 12 : 0) + (loadBar.visible ? 4 : 0)
        ToolButton { x: 0; y: 0; width: 48; height: 48; text: "‹"; font.pixelSize: 24; enabled: web.canGoBack; onClicked: web.goBack() }
        Label { x: 48; y: 0; width: Math.max(0, parent.width - 192); height: 48; verticalAlignment: Text.AlignVCenter; text: login.host; elide: Text.ElideRight; color: "white"; renderType: Text.NativeRendering }
        ToolButton { x: parent.width - 144; y: 0; width: 96; height: 48; text: qsTr("Finish"); onClicked: login.check() }
        ToolButton { x: parent.width - 48; y: 0; width: 48; height: 48; text: "×"; font.pixelSize: 24; onClicked: login.cancel() }
        Label {
            id: statusLabel
            x: 12
            y: 54
            width: Math.max(0, parent.width - 24)
            text: login.status
            visible: login.keyboardInset === 0
            color: "#dddddd"
            wrapMode: Text.Wrap
            font.pixelSize: 13
            renderType: Text.NativeRendering
        }
        ProgressBar { id: loadBar; x: 0; y: parent.height - height; width: parent.width; height: 4; from: 0; to: 100; value: web.loadProgress; visible: web.loading }
    }
    WebEngineView {
        id: web
        objectName: "signInWebView"
        anchors.fill: parent
        anchors.bottomMargin: login.keyboardInset
        profile: loginProfile
        url: login.initialUrl
        userScripts: [WebEngineScript {
            name: "Keep focused field visible"
            injectionPoint: WebEngineScript.DocumentReady
            worldId: WebEngineScript.ApplicationWorld
            runOnSubframes: false
            // Only scroll geometry. No text, passwords or form values are read.
            sourceCode: "(() => { let timer; const reveal = () => { clearTimeout(timer); timer = setTimeout(() => { const e = document.activeElement; if (e && e.matches('input:not([type=hidden]), textarea, [contenteditable=true]')) e.scrollIntoView({block:'center', inline:'nearest', behavior:'instant'}); }, 120); }; window.addEventListener('resize', reveal); document.addEventListener('focusin', reveal); reveal(); })();"
        }]
        audioMuted: true
        settings.fullScreenSupportEnabled: false
        settings.screenCaptureEnabled: false
        onUrlChanged: login.page(url)
        onLoadingChanged: function(info) {
            if (info.status === WebEngineView.LoadSucceededStatus) login.loaded()
        }
        onNavigationRequested: function(request) {
            if (request.isMainFrame && !login.allow(request.url)) request.action = WebEngineView.IgnoreRequest
        }
        onNewViewRequested: function(request) {
            if (login.allow(request.requestedUrl)) request.openIn(web)
        }
        onFeaturePermissionRequested: function(origin, feature) { web.grantFeaturePermission(origin, feature, false) }
        onCertificateError: function(error) { error.rejectCertificate() }
    }
}
