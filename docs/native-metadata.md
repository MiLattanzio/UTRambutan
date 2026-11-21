# Metadati nei controlli Ubuntu Touch — 0.1.5

Media Hub espone `Metadata` in sola lettura: titolo, artista e copertina devono
provenire dal contenitore audio. Un secondo server MPRIS nell'app perderebbe
la possibilità di rispondere e aggiornarsi quando Lomiri sospende il processo.

`MediaHubTrackPreparer` prepara una copia privata di ogni brano della coda con
titolo, artista, album quando disponibile e copertina incorporata. Il codec viene
trasmesso dal coordinatore insieme alla richiesta audio. Opus/Vorbis usano Ogg,
AAC/ALAC M4A, MP3 il proprio contenitore e FLAC il proprio: l'audio viene copiato
senza ricodifica. Per altri codec locali viene usato FLAC, senza perdita ulteriore.
Le immagini sono convertite in JPEG e incorporate nei metadati del contenitore.

La copia viene completata prima di essere consegnata a Media Hub. Sul telefono
la sorgente attende il riempimento della cache e condivide il download fra
richieste concorrenti; con cache disabilitata la preparazione scarica una copia
temporanea. Questo può aumentare il tempo del primo avvio e lo spazio occupato
dalla coda rispetto agli URL remoti. I file preparati restano nella cartella
privata `media-hub` fino alla sostituzione della riproduzione. Ogni URI è univoco,
per evitare che il thumbnailer riusi la copertina del brano precedente.

Le preparazioni dei successori avvengono fuori dalla coda dei comandi D-Bus;
un nuovo play annulla quelle superate. L'assenza o il mancato download della
copertina non impedisce la riproduzione né la pubblicazione di titolo e artista.

Lomiri legge gli stessi dati attraverso il player MPRIS di sistema e l'indicatore
Ayatana Sound, comprese le azioni del menu `phone_greeter` a telefono bloccato.
Il servizio Media Hub continua a identificarsi come Media Player: il nome della
sua desktop entry è fissato dal servizio del sistema. Non viene avviato un
processo aggiuntivo per mantenere attiva l'app.

## Verifiche

La prova preliminare sul Fairphone ha verificato che il thumbnailer estragga
effettivamente la copertina dal contenitore Ogg; la sola presenza di `mpris:artUrl`
non era sufficiente, perché il contenitore Matroska non veniva letto da quel
thumbnailer. La copertina estratta corrisponde a quella di “Nato sbagliato”.
È stata verificata anche l'estrazione della copertina da un campione AAC/M4A.
Entrambi i campioni restituiscono l'immagine attesa dal servizio nativo.

Suite finale: **4.245 test, 4.215 superati, 30 esclusi, zero errori**. I nuovi casi
coprono metadati distinti per righe duplicate, cancellazione della preparazione
dei successori e un unico download completo condiviso. La build Release con
`-warnaserror` non presenta errori né avvisi.

Le evidenze locali sono in `artifacts/verification/metadata-*`.

## App installata sul Fairphone 5

Installata la 0.1.5 con profilo AppArmor `enforce`; gli hash di Core, Streams e
Integrations coincidono con il publish. Click SHA-256:
`fe6431d459057481706639bed778870fa018089280576aedaa11dfea1526a361`.
La review Click mantiene il limite preesistente della policy `ubuntu/2404.2`
non riconosciuta dal suo database; non è un'approvazione OpenStore.

Nella sessione nativa 22, PID 14542, “Nato sbagliato” / “Articolo 31” compare
con la copertina corretta nel pannello Suono, verificato visivamente. Tutti gli
otto brani della coda sono stati preparati. Media Hub, MPRIS e le azioni Ayatana
per `phone` e `phone_greeter` riportano gli stessi dati.

Dopo aver portato il primo brano vicino alla fine, il servizio è passato a
“BESTIE” / “J-AX” e ha aggiornato anche la copertina con Rambutan in stato
`T (stopped)`: la lettura successiva mostra Track/1 a 35,953 secondi.
AccountsService riceve dal servizio audio titolo, artista, stato e URL immagine
aggiornati, senza interventi dell'app sospesa.

### Verifica visiva del blocco normale

Dopo aver predisposto il blocco con il pulsante fisico e aperto Suono, l'utente
ha confermato il telefono bloccato. La cattura `metadata-real-lock.png` mostra
“Salsa”, “J-AX, Jake La Furia” e la copertina corretta. Nella sessione 0, PID 5500,
Rambutan rimane in `T (stopped)` durante tutte le prove dal pannello:

- Pausa a 51,744 secondi e ripresa a 53,026 secondi.
- Comando successivo: “The Pub Song” / “J-AX”, nuova copertina, stato `Playing`
  a 2,214 secondi. La visualizzazione è confermata in `metadata-real-lock-next.png`.

Il blocco normale del telefono non è stato dedotto da `ScreenSaver.GetActive`
o `Shell.Session.IsLocked`: su questo dispositivo tali API non coincidevano
con lo stato visibile. Il comando D-Bus `Shell.Session.Lock` aveva aperto una
sessione di accesso separata, senza player nell'indicatore; l'utente è tornato
alla sessione telefonica e ha predisposto il blocco normale per la prova finale.
Non sono state modificate impostazioni di autenticazione, privacy o servizi
del sistema. Al termine il nuovo brano continua a suonare, con il pannello
Suono lasciato sulla schermata bloccata.

Riferimenti: [Media Hub UBports](https://gitlab.com/ubports/development/core/media-hub),
in particolare `player_implementation.cpp`, `player_skeleton.h` e
`mpris/media_player2.cpp`; sul dispositivo è stato controllato anche
`/usr/share/lomiri/Panel/Indicators/IndicatorMenuItemFactory.qml`, che legge
`title`, `artist` e `art-url` dalle azioni dell'indicatore.
