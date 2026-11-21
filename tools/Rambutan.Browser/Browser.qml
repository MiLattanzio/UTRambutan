import QtQuick
import QtQuick.Window
import QtQuick.Controls
import QtQuick.Layouts
import QtWebEngine

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
    header: ColumnLayout {
        spacing: 4
        RowLayout {
            Layout.fillWidth: true
            ToolButton { text: "‹"; font.pixelSize: 24; implicitWidth: 48; implicitHeight: 48; enabled: web.canGoBack; onClicked: web.goBack() }
            Label { text: login.host; Layout.fillWidth: true; elide: Text.ElideRight; color: "white"; renderType: Text.NativeRendering }
            ToolButton { text: qsTr("Finish"); implicitHeight: 48; onClicked: login.check() }
            ToolButton { text: "×"; font.pixelSize: 24; implicitWidth: 48; implicitHeight: 48; onClicked: login.cancel() }
        }
        Label {
            text: login.status
            visible: login.keyboardInset === 0
            color: "#dddddd"
            wrapMode: Text.Wrap
            Layout.fillWidth: true
            Layout.leftMargin: 12
            Layout.rightMargin: 12
            Layout.bottomMargin: 6
            font.pixelSize: 13
            renderType: Text.NativeRendering
        }
        ProgressBar { Layout.fillWidth: true; from: 0; to: 100; value: web.loadProgress; visible: web.loading }
    }
    WebEngineView {
        id: web
        anchors.fill: parent
        anchors.bottomMargin: login.keyboardInset
        profile: loginProfile
        url: login.initialUrl
        userScripts.collection: [{
            name: "Keep focused field visible",
            injectionPoint: WebEngineScript.DocumentReady,
            worldId: WebEngineScript.ApplicationWorld,
            runsOnSubFrames: false,
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
            if (request.isMainFrame && !login.allow(request.url)) request.reject()
        }
        onNewWindowRequested: function(request) {
            if (login.allow(request.requestedUrl)) request.openIn(web)
        }
        onPermissionRequested: function(permission) { permission.deny() }
        onCertificateError: function(error) { error.rejectCertificate() }
    }
}
