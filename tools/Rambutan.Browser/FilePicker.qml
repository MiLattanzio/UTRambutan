import QtQuick 2.4
import QtQuick.Window 2.2
import Lomiri.Components 1.3
import Lomiri.Content 1.3

Window {
    id: root
    visible: false
    width: 360
    height: 640
    property var transfer: null
    property bool finishing: false

    function cancel() {
        if (finishing) return
        finishing = true
        if (transfer) transfer.state = ContentTransfer.Aborted
        pickerBridge.cancel()
    }

    ContentPeerPicker {
        anchors.fill: parent
        contentType: ContentType.All
        handler: ContentHandler.Source
        onCancelPressed: root.cancel()
        onPeerSelected: {
            peer.selectionType = ContentTransfer.Single
            root.transfer = peer.request()
        }
    }
    ContentTransferHint { anchors.fill: parent; activeTransfer: root.transfer }
    Connections {
        target: root.transfer
        onStateChanged: {
            if (root.finishing || !root.transfer) return
            if (root.transfer.state === ContentTransfer.Charged) {
                root.finishing = true
                var items = root.transfer.items
                var saved = items.length === 1 && pickerBridge.collect(items[0].url)
                // Collect copies into our private scratch directory before
                // Finalize removes Content Hub's transient incoming files.
                root.transfer.finalize()
                pickerBridge.finish(saved)
            } else if (root.transfer.state === ContentTransfer.Aborted) {
                root.cancel()
            }
        }
    }
    onClosing: root.cancel()
}
