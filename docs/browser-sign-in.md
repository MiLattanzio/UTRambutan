# Accesso dal browser — 0.1.10

Il dialogo AXAML offre un pulsante per aprire YouTube Music in un componente
Qt WebEngine 5.15 incorporato nella stessa finestra. L'utente completa personalmente accesso e
consenso nelle pagine Google. Non occorrono F12, OAuth o un client Google Cloud.
L'importazione manuale dei cookie resta disponibile nella sezione avanzata.

## Trasferimento e ciclo di vita

- `BrowserCookieSignIn` avvia `tools/browser/rambutan-browser` con pipe anonime
  ereditate. Nessun cookie passa in argomenti, clipboard, log o file intermedi.
- `QQuickWebEngineProfile` usa `offTheRecord`, cache in memoria e nessun cookie
  persistente. Non legge né modifica il profilo di Morph.
- Il cookie store nativo vede anche i cookie HttpOnly. Vengono accettati solo
  cookie validi per `youtube.com` e `music.youtube.com`, con percorso `/`;
  i cookie degli account Google non vengono esportati. La navigazione principale
  è limitata a HTTPS su Google e YouTube. Download e richieste di permessi sono
  rifiutati; il browser è muto.
- Quando arrivano cookie di autenticazione di YouTube, l'app verifica la sessione
  tramite il normale client InnerTube, anche se il redirect termina su un altro
  dominio consentito. Attende 750 ms di stabilità dell'insieme dei cookie. Solo una
  risposta valida sostituisce e salva la sessione; il browser si chiude da solo.
  Le verifiche non riuscite vengono ripetute anche con gli stessi cookie, dopo
  2, 4, 8, 16 e poi 30 secondi, senza reinserire le credenziali. **Finish** forza
  un nuovo controllo e, se necessario, torna a Music con la sessione privata
  già aperta. Il cookie store viene aggiornato anche al completamento delle pagine.
  Annullare conserva
  l'accesso precedente. Un timeout di 15 minuti chiude il tentativo.
- Chiudere Rambutan o annullare l'operazione termina il componente. Il profilo
  web privato viene scartato. La sessione importata usa lo stesso CookieVault
  dell'accesso manuale: keyring se disponibile, altrimenti file privato 0600,
  come dichiarato nel dialogo.

Le API Qt usate sono documentate in
[QWebEngineCookieStore](https://doc.qt.io/archives/qt-5.15/qwebenginecookiestore.html) e
[QQuickWebEngineProfile](https://doc.qt.io/archives/qt-5.15/qquickwebengineprofile.html).
Il componente mantiene la verifica TLS e non apre porte di debug.

## Build e verifiche

`scripts/build-click.sh` compila anche il componente C++/QML ARM64 con
`scripts/build-browser.sh`. Qt 5 viene risolto dalle librerie di Ubuntu Touch;
il Click aggiunge il gruppo AppArmor standard `webview`. Nessuna modifica alla
rootfs del telefono. Il resto dell'app continua a usare Avalonia e UTDotNet.
Il componente usa esplicitamente lo stile Qt Quick Controls 2 **Default** e una
palette propria; il launcher del telefono seleziona altrimenti **Suru**.
Usa il backend X11/XWayland della finestra Avalonia, con scala `GRID_UNIT_PX / 8`.
Il dialogo passa soltanto il proprio XID al componente, che crea una finestra
figlia nativa e ne aggiorna la geometria; il processo resta separato. Non viene
creata una seconda finestra principale. Una finestra separata nello stesso gruppo di processi Click può far fallire
la verifica del focus di Maliit: QtMir cerca una sessione tra tutti i processi
del gruppo e può selezionare quella Avalonia non attiva. Il controllo è nel
[codice ufficiale di QtMir](https://gitlab.com/ubports/development/core/qtmir/-/blob/main/src/modules/QtMir/Application/dbusfocusinfo.cpp).
Il browser riserva spazio alla tastiera Maliit normalizzando la scala delle
coordinate native o logiche. Il calcolo include la parte del dialogo che può
superare l'area visibile di Lomiri. Uno script isolato scorre fino all'elemento
con focus dopo il ridimensionamento: legge il tipo dell'elemento, non i valori
dei campi. Il banner informativo cede spazio alla tastiera.

La barra del browser usa larghezze esplicite e un'altezza calcolata dal testo
informativo. La combinazione precedente di `ColumnLayout`, `RowLayout` e testo
a capo causava in Qt 5 un ciclo di dimensionamento (`Polish loop detected` /
`Detected recursive rearrange`), lasciando vuota l'area web. Questo difetto è
stato riprodotto con la pagina locale, corretto e verificato sul Fairphone 5.
La grafica dell'app e del dialogo rimane in AXAML.

Gli eventi diagnostici dell'app riportano avvio, uscita, stato della verifica,
consegna della sessione al dialogo ed esito del salvataggio; non registrano
cookie, messaggi grezzi del motore web o eccezioni con dati.

- Build Release con avvisi trattati come errori: riuscita, zero avvisi.
- 89 test mirati: trasferimento tra processi, cancellazione, validazione,
  sessione, persistenza e importazione manuale; tutti superati.
- Test nativo ARM64 eseguito sul Fairphone 5: filtri dei cookie e dei domini,
  coordinate della tastiera e pianificazione dei tentativi automatici superati
  usando esclusivamente dati sintetici.
- Dialogo AXAML e browser incorporato verificati sul Fairphone 5.
  `FocusInfo.isPidFocused` del processo browser restituisce `true`; la tastiera
  Maliit è stata osservata durante l'accesso e il campo riceve testo.
- La 0.1.9 è stata provata con una pagina locale contenente campi fittizi in alto,
  password e in fondo: tutti interamente visibili con Maliit aperta. La pagina
  segnala `visible=true`; il viewport cambia correttamente anche passando alla
  tastiera password. Chiusura del browser e riapertura verificati nello stesso
  dialogo, senza chiudere Rambutan.
- La fixture si compila con `RAMBUTAN_BROWSER_INPUT_TEST=1 scripts/build-click.sh`;
  il pacchetto normale usa un altro eseguibile e non include la pagina fittizia
  né la diagnostica geometrica. `qmllint` controlla il QML durante ogni build.
- App e browser sono confinati sotto
  `rambutan.milattanzio_rambutan_0.1.9 (enforce)`. Nessun profilo unconfined o
  permesso personalizzato è stato aggiunto.
- L'accesso reale della 0.1.9 è stato completato: alle 22:46 UTC del 21 novembre
  2026 i log confermano sessione valida, consegna al dialogo e salvataggio; il
  successivo riavvio ha confermato la sessione persistente.

Artefatti della precedente 0.1.9 verificati sul dispositivo:

- Click: `1c6cd4ba003c9a023c01dc0076d93099ffbb43278ee4921d43a096344470d65b`.
- Assembly app: `339d980ae06a801ba7a10c88935a0ac5a5ee71267124d85d3a44b97385a1cae4`.
- Browser: `487382262a11643dcd4304d238edb6bb1bdeff268631bd79d56de9227f8a32e9`.

Build e test sono conservati in `artifacts/verification/browser-final-build.log`,
`browser-tests.log` e `browser-release-build.log`, esclusi da Git. Le immagini
`rambutan-input-fixed-*.jpg` mostrano esclusivamente i campi fittizi. Non vengono
conservate immagini del modulo con i dati di accesso.

La review della precedente 0.1.9 non è superata: il tool non riconosce la policy
`ubuntu/2404.2` e segnala Qt 6 come troppo recente per il framework. Le librerie
Qt del telefono sono state controllate direttamente; questo non equivale
all'approvazione OpenStore.

## Verifica del port Qt 5 — 22 novembre 2026

- Build ARM64 contro Qt base 5.15.13 e Qt WebEngine 5.15.19 del framework
  Ubuntu Touch 24.04; il browser dipende soltanto dalle librerie Qt 5.
- Adattati import QML, script di pagina, navigazione, nuove finestre e gestione
  dei permessi alle API Qt 5. Il rifiuto di navigazione usa
  `WebEngineView.IgnoreRequest`; gli script usano `runOnSubframes`.
- Test nativi ARM64 di dominio/cookie, geometria e tentativi automatici: superati.
- Fixture installata nel Click con AppArmor attivo: dopo la correzione del layout
  la pagina è visibile, confermata anche dall'utente. Campo normale con tastiera:
  viewport 400, campo 172–229, `visible=true`; password: viewport 432, campo
  188–245, `visible=true`. Immagini locali di soli campi fittizi in
  `artifacts/verification/browser-qt5/top-field.png` e `password-field.png`
  (escluse da Git; non sono screenshot per lo store).
- La build normale esclude la risorsa HTML fittizia. Browser SHA-256:
  `ba4592ee62839c11fd428166434c33b232c8673c12ffed3a9d13819ff31a2f2c`.
- Click di produzione SHA-256:
  `2154e8c173c4901538a7c1dbe292da388b0992faa88b9366feac336b9314e6a9`.
- Il validatore locale non segnala più Qt troppo recente. Rimane l'assenza
  della policy `ubuntu/2404.2` nel validatore locale; non equivale a una review
  OpenStore riuscita. Pubblicazione e screenshot per lo store sono sospesi
  durante la verifica del browser.
- Accesso Google e importazione automatica con Qt 5: riusciti, confermati
  dall'utente e dai log del 21 novembre alle 23:27:19–23:27:22 UTC (22 novembre,
  00:27 locali). Sessione `Valid`, consegna e accettazione nel dialogo,
  salvataggio senza errori (`Cookie session storage: None`), browser terminato.
  Nessuna schermata è stata acquisita durante l'inserimento delle credenziali.
- Riavvio della build normale: alle 23:28:08 UTC la sessione salvata risulta
  ancora valida; alle 23:28:09 UTC i cookie sono stati aggiornati normalmente.

Log di build: `artifacts/verification/browser-qt5-layout-click.log` e
`artifacts/verification/browser-qt5-production-build.log` (esclusi da Git).
