# Verifica del porting — 21 novembre 2026

Questo documento conserva la verifica iniziale della versione 0.1.0.
La versione 0.1.1 integra Media Hub: implementazione, prove di sospensione,
display spento e nuovi risultati sono in [media-hub.md](media-hub.md).

## Ambiente

- Host: .NET SDK 10.0.401, runtime 10.0.12, Linux x64.
- Dispositivo ADB: Fairphone 5 `49f88e70`, aarch64, Ubuntu Touch con base
  Ubuntu 24.04.5. Sessione Lomiri/Mir, utente phablet UID 32011.
- Display osservato: 1224×2700, GridUnit 24, scala Avalonia 3, handle `XID`.
- Pacchetto: `rambutan.milattanzio`, hook `rambutan`, versione 0.1.0.
- NuGet: Avalonia 12.1.6 e UTDotNet 0.1.5-preview.1, feed pubblico, cache del
  nuovo repository. Nessuna dipendenza dai sorgenti esterni.

## Risultati osservati

| Prova | Risultato e perimetro |
| --- | --- |
| Build Release | Riuscita senza errori o avvisi, anche con `-warnaserror`. |
| Suite completa | 4.195 superati, 30 esclusi, zero errori su 4.225. Gli esclusi richiedono Windows, FFmpeg x64 o un Secret Service di prova. |
| Verifica successiva dell'app | 1.742 test dell'app superati, inclusi due nuovi casi sulla conferma di copia. Gli altri progetti non sono cambiati dopo la suite completa. |
| Layout automatico | Ricerca a 360×640, 408×865 con inset 300, 900×384 con inset 220; campo da almeno 48 unità ancora raggiungibile. |
| Input desktop | Avvio reale della shell telefonica in demo sul desktop Wayland: scala 1, `native=False`, rendering controllato. Nei test headless l’IME Avalonia resta abilitato. Nessuna prova con tastiera desktop fisica. |
| Publish | ARM64 self-contained, runtime incluso, nessun AOT/trimming/single-file. |
| Avvio di sviluppo sul telefono | Home e catalogo reale caricati; scala 3; rendering applicativo controllato. |
| Installazione Click | Installato tramite `com.lomiri.click.Install` e avviato tramite `lomiri-app-launch`. |
| Confinamento | `/proc/7639/attr/current`: `rambutan.milattanzio_rambutan_0.1.0 (enforce)` durante la prova del Click. |
| Tastiera | Tap sul campo Cerca, connessione Maliit, tastiera nativa visibile nella cattura autorizzata; query `jazz` immessa, backspace, reinserimento e Return funzionanti. |
| Risultati | Catalogo YouTube Music reale; risultati ottenuti dalla ricerca immessa con Maliit. Nessun account utilizzato. |
| Riproduzione di sviluppo | MPRIS riporta `Playing` e posizione superiore a 15 secondi; dispositivo audio inizializzato. |
| Riproduzione Click | Player mostra pausa durante la riproduzione; flusso PulseAudio del PID Rambutan non sospeso e non in mute, stereo 48 kHz. Non è una valutazione dell'ascolto o della qualità sonora. |
| Tastiera e layout | Risultati vincolati sopra l'area Maliit; header e navigazione cedono spazio alla tastiera. Hide nativo confermato nei log dopo il cambio focus. |
| Copia applicativa | Test automatici verificano che un fallimento di copia non produca una notifica di successo. Selezione/Cut/Paste fisici della nuova app non verificati. |
| Review Click | Due errori del tool per metadati della policy `2404.2` mancanti. Nessuna approvazione OpenStore. |

Il test usa tap iniettati nel touchscreen, non input sintetico scritto nel TextBox.
Le immagini del compositor sono state autorizzate dall'utente per Rambutan e la
tastiera. I rendering `fp5-app.png`, `fp5-search.png` e `fp5-results.png` sono invece
prodotti dalla sola app e non provano la visibilità della tastiera del sistema.

## Evidenze

File locali, esclusi da Git, in `artifacts/verification/`:

- `build.log`, `tests.log`, `app-tests-final.log`, `install.log`, `click-build.log`;
- `fp5-app.png`, `fp5-search.png`, `fp5-keyboard.png`, `fp5-results.png`;
- `click-home.png`, `click-playback.png`, `desktop-phone.png`, `rambutan-audio.json`;
- log di UTDotNet senza contenuti digitati o credenziali.

Hash e revisione dell'artefatto consegnato sono rispettivamente in
`artifacts/click/SHA256SUMS` e `artifacts/click/review.txt`.
Il digest Docker è registrato in `artifacts/click/build-image.txt`.

## Limiti non coperti dalle prove

- Rotazione fisica, sospensione profonda, sessioni prolungate e ripresa audio.
- Account autenticato, gestione della libreria personale e accesso a file esterni.
- Content Hub e Media Hub: non integrati. MPRIS desktop è negato dal profilo Click;
  non sono stati aggiunti permessi non standard né modificate policy o rootfs.
- Password/email/numerico, composizione multilingua, dialoghi con tastiera,
  selezione con maniglie e scambio appunti con app Qt: nessuna certificazione fisica
  della nuova app; non si trasferiscono automaticamente i risultati della demo UTDotNet.
- Avvio su altre immagini Ubuntu Touch o architetture diverse.

La review del Click termina con codice 2 perché `click-reviewers-tools` non ha
`ubuntu/2404.2` nella propria tabella. Il validatore desktop emette inoltre il
messaggio sull'estensione dell'icona relativa, mantenuta come richiesto dal formato
Click. Entrambi gli output vengono conservati separatamente dal successo del build.

Artefatto finale: SHA-256 `aab4ab1bb41079d500bde4be5ca3c9eb062bea1fc853ebb219bf03e38ec7fa32`.

Reinstallazione finale riuscita: SHA-256 dell’assembly sul telefono e nel publish
`2b2c5960271fd1d64c6ff4d7c54fbd4bfb8ed229400fb0ffcd7b907e3a8236e7`.

Riavvio finale osservato con PID 8168: stesso profilo AppArmor `enforce`, scala 3,
backend XID e avvio senza eccezioni. L’app resta installata sul telefono.

Durante il prebuffer di un brano successivo YouTube ha restituito HTTP 403; il
resolver originale ha avviato il tentativo con un URL aggiornato. La prova di un
brano riprodotto non garantisce quindi la disponibilità di tutti gli stream.
