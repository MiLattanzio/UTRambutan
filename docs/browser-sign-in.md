# Accesso dal browser — 0.1.9

Il dialogo AXAML offre un pulsante per aprire YouTube Music in un componente
Qt WebEngine 6 incorporato nella stessa finestra. L'utente completa personalmente accesso e
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
[QWebEngineCookieStore](https://doc.qt.io/qt-6/qwebenginecookiestore.html) e
[QQuickWebEngineProfile](https://doc.qt.io/qt-6/qquickwebengineprofile.html).
Il componente mantiene la verifica TLS e non apre porte di debug.

## Build e verifiche

`scripts/build-click.sh` compila anche il componente C++/QML ARM64 con
`scripts/build-browser.sh`. Qt 6 viene risolto dalle librerie di Ubuntu Touch;
il Click aggiunge il gruppo AppArmor standard `webview`. Nessuna modifica alla
rootfs del telefono. Il resto dell'app continua a usare Avalonia e UTDotNet.
Il componente usa esplicitamente lo stile Qt Quick **Basic**: il launcher del
telefono imposta **Suru**, presente per Qt 5 ma assente per Qt 6 su questa immagine.
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
- L'accesso reale e la verifica automatica della sessione sono ancora in corso.

Artefatti verificati sul dispositivo:

- Click: `1c6cd4ba003c9a023c01dc0076d93099ffbb43278ee4921d43a096344470d65b`.
- Assembly app: `339d980ae06a801ba7a10c88935a0ac5a5ee71267124d85d3a44b97385a1cae4`.
- Browser: `487382262a11643dcd4304d238edb6bb1bdeff268631bd79d56de9227f8a32e9`.

Build e test sono conservati in `artifacts/verification/browser-final-build.log`,
`browser-tests.log` e `browser-release-build.log`, esclusi da Git. Le immagini
`rambutan-input-fixed-*.jpg` mostrano esclusivamente i campi fittizi. Non vengono
conservate immagini del modulo con i dati di accesso.

La review Click non è superata: il tool non riconosce la policy
`ubuntu/2404.2` e segnala Qt 6 come troppo recente per il framework. Le librerie
Qt del telefono sono state controllate direttamente; questo non equivale
all'approvazione OpenStore.
