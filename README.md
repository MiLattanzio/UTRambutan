# Rambutan per Ubuntu Touch

Port autonomo di Rambutan Music Player per Ubuntu Touch 24.04 ARM64. Il codice
deriva da Rambutan 0.1.0-beta.11; la provenienza è in [UPSTREAM.md](UPSTREAM.md).
Non occorrono né la cartella originale `Rambutan` né i sorgenti di UTDotNet.

L'interfaccia usa **AXAML**, binding compilati e i view model originali. La finestra
del telefono offre Home, ricerca, libreria, coda, player e impostazioni essenziali.
Il motore conserva catalogo YouTube Music, risoluzione degli stream, decodifica,
playlist, cache, testi e persistenza di Rambutan.

## Compilazione

Richiede .NET SDK 10. Il feed è esclusivamente nuget.org, configurato in
`NuGet.Config`. Avalonia è fissato a **12.1.6** e `UTDotNet.Avalonia` a
**0.1.5-preview.1**. I quattro pacchetti UTDotNet arrivano da NuGet, senza
`ProjectReference` alla libreria. I lock file includono `linux-x64` e `linux-arm64`.

```bash
export NUGET_PACKAGES="$PWD/.nuget/packages"
dotnet restore UTRambutan.slnx --configfile NuGet.Config --locked-mode
dotnet build UTRambutan.slnx -c Release --no-restore -warnaserror
dotnet test --solution UTRambutan.slnx -c Release
```

Per avviare su un desktop Linux con le dipendenze native Avalonia:

```bash
dotnet run --project src/Rambutan.App -c Release --no-build
# Anteprima della stessa interfaccia telefonica, con input desktop normale:
RAMBUTAN_PHONE_PREVIEW=1 dotnet run --project src/Rambutan.App -c Release --no-build
```

`RAMBUTAN_DATA_DIR` e `RAMBUTAN_TOOLS_DIR` permettono di isolare i dati di prova.
Sul desktop il primo avvio prepara le dipendenze audio; nel Click sono già incluse.
La modalità demo originale rimane disponibile con `RAMBUTAN_DEMO=anonymous`:
usa le fixture copiate in `tests/Rambutan.InnerTube.Tests/Fixtures`.

## Click ARM64

Occorrono Docker, Python 3, `desktop-file-validate` e l'immagine
`clickable/amd64-ut24.04-2.x-arm64:latest`. L'immagine effettivamente usata viene
registrata con il suo digest in `artifacts/click/build-image.txt`.

```bash
scripts/build-click.sh
scripts/install-click.sh SERIAL_ADB
```

Il primo comando pubblica `linux-arm64` self-contained, **senza trimming, AOT o
single-file**, verifica gli SHA-256 delle dipendenze native e costruisce:

`artifacts/click/rambutan.milattanzio_0.1.11_arm64.click`

FFmpeg 9.0.2, yt-dlp 2026.10.26 e Deno 2.9.8 sono inclusi; versioni, URL e hash
sono in `packaging/native-tools.json`. Sul telefono non si installano SDK o
librerie nella rootfs. Gli strumenti inclusi non si autoaggiornano: vengono
aggiornati con il Click. Il componente di accesso viene compilato con
`scripts/build-browser.sh` nell'immagine `utrambutan-browser-qt5-build:ut24.04`,
preparata automaticamente dal Dockerfile in `tools/Rambutan.Browser`.
Usa Qt WebEngine 5.15 fornito dal sistema Ubuntu Touch; non incorpora un'altra copia
del motore. Le versioni Qt del build sono in `artifacts/native-browser/qt-versions.txt`.
L'app richiede i gruppi AppArmor standard `networking`, `audio` e `webview`,
con policy `2404.2` e framework `ubuntu-touch-24.04-2.x`.

Il build conserva il risultato completo della review e ne restituisce il codice
di uscita. Il tool attualmente disponibile termina con **2**: non conosce
`ubuntu/2404.2`;
il Click viene comunque generato. Non è una review superata né
un'approvazione OpenStore. Anche l'avviso specifico di `desktop-file-validate`
sull'icona relativa richiesta dal formato Click è conservato; altri errori
desktop interrompono il build.

Aprire **Rambutan** dal launcher Lomiri. Per un avvio CLI usare l'ambiente della
sessione effettiva e l'ID `rambutan.milattanzio_rambutan_0.1.11`; non fissare UID,
display o seriale di un altro telefono nel launcher distribuito.

## Integrazione Ubuntu Touch

- `Rambutan.Integrations/UbuntuTouch/MediaHubAudioPlayer.cs` affida audio e coda
  al servizio di sistema `com.lomiri.MediaHub.Service`. Il servizio prosegue
  anche con il processo Avalonia sospeso e il display spento. Il desktop mantiene
  `EngineAudioPlayer` e MPRIS; sul telefono non si registra un server MPRIS proprio.
- La coda viene risolta progressivamente e trasferita alla playlist nativa.
  I file locali vengono copiati nell'area privata `media-hub` per proteggerli
  dall'espulsione della cache. Al rientro, brano, posizione e pausa vengono letti
  dal servizio, anche se nel frattempo sono avanzati più brani. La chiusura
  dell'app termina la sessione; non lascia un servizio o un player scollegato.
- `Services/UbuntuTouchPlatform.cs` rileva Ubuntu Touch e applica `GridUnit / 8`
  prima di inizializzare Avalonia, rispettando gli override di scala.
- `Program.cs` usa X11/XWayland e popup incorporati su Ubuntu Touch; sul desktop
  mantiene il backend normale e non attiva Maliit.
- `Views/Phone*.axaml` e `Styles/Phone.axaml` definiscono la grafica. Il codice C#
  collega navigazione, ciclo di vita e servizi, senza costruire la nuova UI.
- `Services/PhoneInput.cs` collega gli editor AXAML a `TextInputAdapter` e a un
  `MaliitInputBridge` condiviso dai dialoghi. Le registrazioni vengono eliminate
  al distacco della pagina e ricreate al ritorno; clipboard e menu si liberano
  con il campo. L'inset usa coordinate globali, scala effettiva e intersezione
  `MaliitLayout.BottomInset`, anche al ridimensionamento.
- Il launcher usa built-in della shell e imposta `RESOURCE_NAME`. Lomiri gestisce
  l'istanza dell'app; Velopack e la tray desktop non vengono usati sul telefono.
- Dati, impostazioni e log rimangono in
  `$XDG_DATA_HOME/rambutan.milattanzio` (normalmente
  `~/.local/share/rambutan.milattanzio`). Impostazioni e coda sono salvate anche
  alla disattivazione della finestra.

`RAMBUTAN_RENDER_SNAPSHOT=/percorso/app.png` abilita una sonda di sviluppo che
renderizza **solo la finestra dell'app** ogni tre secondi. Non acquisisce Maliit,
il compositor o altre applicazioni; non è attiva nel launcher Click.

## Verifiche e limiti

Le prove effettuate e le evidenze locali sono descritte in
[docs/device-validation.md](docs/device-validation.md). La verifica riguarda il
Fairphone 5 connesso, non tutti i dispositivi Ubuntu Touch.
La correzione dell'avvio audio HTTP 403 nella versione 0.1.2 è descritta in
[docs/audio-start-fix.md](docs/audio-start-fix.md).
La versione 0.1.3 rispetta il volume multimediale di Ubuntu Touch all'avvio e al
cambio brano; diagnosi e prove sono in [docs/volume-start-fix.md](docs/volume-start-fix.md).
Nella versione 0.1.4 il player telefonico usa i controlli volume di sistema:
il cursore interno è stato rimosso dalla vista AXAML.

La shell telefonica espone le impostazioni essenziali; le viste e le funzioni
desktop avanzate sono conservate nei sorgenti. Media Hub non offre il crossfade,
l'equalizzatore o la normalizzazione del motore desktop: sul telefono non vengono
applicati né proposti come disponibili. La transizione tra stream può richiedere
buffering; non è garantita una riproduzione gapless.

In background possono proseguire i brani già trasferiti al servizio. La risoluzione
di nuovi URL e l'estensione radio richiedono che l'app sia eseguibile; una coda
ancora in preparazione o URL YouTube scaduti possono richiedere di riaprire l'app.
`Native queue ready` nel log indica il completamento del trasferimento. Le prove
non certificano sessioni di molte ore, chiamate, Bluetooth o consumo energetico.
Content Hub e l'accesso ai file esterni al sandbox restano da integrare. I comandi
D-Bus nativi sono verificati; titolo, artista e copertina vengono incorporati
nei file preparati per il player nativo. Vedere [docs/native-metadata.md](docs/native-metadata.md).

La 0.1.6 riduce l'attesa dei brani non in cache con un primo tentativo di
risoluzione mobile e download a blocchi paralleli, conservando il ripiego
standard. Misure e verifiche: [docs/cold-start.md](docs/cold-start.md).

Il protocollo segue i [sorgenti ufficiali Media Hub di UBports](https://gitlab.com/ubports/development/core/media-hub):
le proprietà di posizione e durata sono in nanosecondi, mentre `Seek` è assoluto
in microsecondi. Le API sono state controllate anche sul servizio del Fairphone.

L'accesso usa esclusivamente i cookie, senza client OAuth. Il pulsante
**Open YouTube Music sign-in** apre un browser privato dedicato: dopo l'accesso,
Rambutan importa e verifica automaticamente i cookie di YouTube Music. Il dialogo
principale resta AXAML; solo la finestra del motore web usa Qt Quick.
L'importazione manuale rimane nella sezione avanzata. Architettura e prove:
[docs/browser-sign-in.md](docs/browser-sign-in.md).
In assenza di keyring, i cookie vengono salvati in file privati con permessi 0600,
come indicato nel dialogo. Lo scambio clipboard con app Qt native resta un limite
noto della preview UTDotNet e non è certificato da una copia interna riuscita.

Licenza GPL-3.0-or-later. Vedere `LICENSE`, `THIRD-PARTY-NOTICES.md` e `licenses/`.
