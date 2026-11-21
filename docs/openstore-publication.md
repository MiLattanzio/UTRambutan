# Pubblicazione OpenStore della 0.1.9

Il Click ARM64 è stato caricato tramite l'account OpenStore del manutentore.
Il server lo ha respinto con il messaggio:

> Linked Qt version too new for framework used: app/tools/browser/rambutan-browser

La scheda è stata creata e compilata ma **non è pubblicata**:
https://open-store.io/manage/rambutan.milattanzio/

Il pacchetto resta quello provato sul Fairphone 5:
`artifacts/click/rambutan.milattanzio_0.1.9_arm64.click`, SHA-256
`1c6cd4ba003c9a023c01dc0076d93099ffbb43278ee4921d43a096344470d65b`.
Non sono stati rimossi controlli, camuffate dipendenze Qt o dichiarati framework
incompatibili per aggirare il rifiuto.

La guida UBports per 24.04-2.0 dichiara sperimentale l'uso di Qt 6 al di fuori di
Morph e non ne garantisce la compatibilità nelle versioni future:
https://forums.ubports.com/topic/12474/changes-in-ubuntu-touch-24.04-2.0-for-app-developers

Per distribuire questa stessa build su OpenStore occorre chiedere ai manutentori
se accettano una revisione manuale. L'alternativa tecnica è portare il componente
browser al framework supportato e ripetere le prove di accesso e tastiera prima
di costruire una nuova versione. Non basta ricompilare contro un'altra release
minore di Qt 6.

La scheda pubblicabile è in `packaging/openstore/listing.json`; il testo per
chiedere la revisione è in `packaging/openstore/manual-review-request.txt`.
Il messaggio non è stato inviato. Le regole di invio sono in
https://open-store.io/submit/.
