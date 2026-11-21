# Rambutan 0.1.11 pubblicata su OpenStore

La versione **0.1.11 ARM64** è pubblicata e disponibile nella
[scheda pubblica OpenStore](https://open-store.io/app/rambutan.milattanzio/).
Pubblicazione confermata il **22 novembre 2026 alle 00:44:55 Europe/Rome**
(21 novembre in UTC). La pagina pubblica mostra la versione 0.1.11,
l'architettura arm64, i tre screenshot e i comandi Download e Install.
Il menu Download offre `v0.1.11 (arm64)`.

## Pacchetto e sorgenti

- Click: `artifacts/click/rambutan.milattanzio_0.1.11_arm64.click`.
- Dimensione: 204151248 byte.
- SHA-256: `8924475a3d1cd2586570d481db6ecc0906f56651559579d1243aefca83852dab`.
- Framework: `ubuntu-touch-24.04-2.x`; policy AppArmor `2404.2`.
- Dipendenze browser: Qt 5.15 e Qt WebEngine di sistema, senza Qt 6.
- [Commit dei sorgenti della release](https://github.com/MiLattanzio/UTRambutan/commit/8e9788a6bf237e13029b6fd891ee0f433d37136e).
- Testo della scheda e changelog: `packaging/openstore/listing.json`.

OpenStore ha accettato il Click 0.1.11 tramite la revisione automatica.
Il vecchio validatore nell'immagine locale continua a segnalare l'assenza della
policy `ubuntu/2404.2` nel proprio database; questo limite del validatore locale
non corrisponde a un rifiuto del server OpenStore.

## Verifiche sul Fairphone 5

Il browser Qt 5 ha completato l'accesso Google, importato i cookie e restituito
il controllo a Rambutan; la sessione è rimasta valida dopo il riavvio.
La pagina sintetica dei campi è stata usata per verificare la tastiera ed è
assente dal pacchetto di produzione. I dettagli sono in
[browser-sign-in.md](browser-sign-in.md).

Durante la preparazione degli screenshot è emerso un problema di spazio vuoto
dopo la chiusura della tastiera nella ricerca. La 0.1.11 aggiorna l'inset quando
cambia il campo attivo: sul telefono, passando dalla ricerca all'album, l'app
torna a occupare tutta l'area disponibile e la navigazione inferiore è visibile.
La build Release è terminata senza errori o avvisi; gli 8 `UbuntuTouchTests`
sono passati dopo la ricompilazione completa del progetto di test.

Nella 0.1.11 installata è stata verificata la riproduzione con Media Hub e la
preparazione della coda nativa di 13 brani. Restano i limiti dichiarati nella
scheda: in background proseguono i brani già preparati; per estendere la coda
o rinnovare URL scaduti può essere necessario riaprire l'app.

## Screenshot pubblicati

Tre catture native del Fairphone 5, dalla build 0.1.11, a 1224 × 2700 pixel:

1. `artifacts/publication/screenshots/01-player.png`: lettore con copertina,
   titolo e artista di “Give Life Back to Music”.
2. `artifacts/publication/screenshots/02-queue.png`: coda dell'album
   “Random Access Memories”.
3. `artifacts/publication/screenshots/03-search.png`: risultati per “daft punk”.

Sono PNG originali del telefono, controllati visivamente prima del caricamento,
senza schermate di anteprima sovrapposte o credenziali. Gli hash sono in
`artifacts/publication/screenshots/SHA256SUMS`. La conferma visiva della scheda
pubblica è `artifacts/publication/openstore-published.jpg`.
Gli artefatti locali sono esclusi da Git; gli screenshot sono disponibili
direttamente nella scheda OpenStore.

## Rifiuto precedente risolto

La 0.1.9 era stata respinta per una dipendenza Qt 6 non accettata dal framework.
Il browser è stato portato a Qt 5 nella 0.1.10, accettata da OpenStore; la
0.1.11 aggiunge la correzione del layout dopo la tastiera. Non sono stati
aggirati i controlli del server. La richiesta di revisione manuale in
`packaging/openstore/manual-review-request.txt` è una bozza storica mai inviata,
superata dal port Qt 5 e dalla pubblicazione riuscita.
