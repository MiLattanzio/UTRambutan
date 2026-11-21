# Media Hub — Ubuntu Touch 0.1.1

## Implementazione

Su Ubuntu Touch `IAudioPlayer` è `MediaHubAudioPlayer`; desktop e anteprima desktop
mantengono `EngineAudioPlayer`. Grafica e binding restano in AXAML e l'integrazione
UTDotNet da NuGet rimane invariata.

`MediaHubConnection` crea una sessione normale tramite D-Bus, configura il ruolo
multimedia e passa gli URI al servizio. Usa `OpenUri`, `Play`, `Pause`, `Stop`,
`Seek`, `Volume`, `LoopStatus` e la `TrackList` nativa. Non registra un nome MPRIS
proprio, non disabilita il confinamento, non modifica la rootfs e non mantiene
il processo Avalonia artificialmente attivo. I gruppi Click rimangono `audio`
e `networking`. La connessione resta aperta durante la sospensione; alla chiusura
dell'app il client richiede esplicitamente `DestroySession`, con attesa limitata
a due secondi, e chiude la connessione. Un handler `SIGTERM` esegue la stessa
pulizia prima della terminazione richiesta da Lomiri. È necessario sul servizio
installato: nella prova iniziale la sola scomparsa del processo non ha fermato
la sessione. Non si usa `DetachSession`.

Le operazioni D-Bus sono ordinate fuori dal thread UI. La risoluzione dei brani
successivi è separata, cancellabile e progressiva. Le modifiche della coda
sostituiscono i successori senza riaprire il brano corrente. I file della cache
usati dalla sessione vengono copiati nell'area privata `media-hub`, perché una
successiva espulsione dalla cache non invalidi la playlist già trasferita.
Le copie vengono ripulite quando inizia una nuova riproduzione.

Il coordinatore riceve una posizione di coda autorevole e gli stati remoti:
al rientro può saltare direttamente al brano raggiunto dal servizio, senza
riprodurre nuovamente i brani passati. Gli eventi mantengono l'identità della riga
anche quando lo stesso video compare più volte. La posizione viene riletta ogni
750 ms quando l'app è eseguibile. Un errore di connessione ricrea la sessione
attraverso il normale percorso di recupero del coordinatore.

Il protocollo è stato verificato sui [sorgenti ufficiali UBports](https://gitlab.com/ubports/development/core/media-hub)
e tramite introspezione sul dispositivo: `Position` e `Duration` sono nanosecondi,
`Seek` è assoluto in microsecondi `uint64`, e gli ID dei metodi TrackList sono
stringhe, non object path MPRIS. I prefissi degli ID in Metadata e TrackList
differiscono: la corrispondenza usa il suffisso identificativo della sessione.

## Fairphone 5: prove del 21 novembre 2026

Dispositivo `49f88e70`, Ubuntu Touch 24.04.5, ARM64, Media Hub
`4.7~20260611100903.31~3b69471+ubports24.04.2`. Click 0.1.1 confinato:
`rambutan.milattanzio_rambutan_0.1.1 (enforce)`.

| Prova | Risultato osservato |
| --- | --- |
| Avvio dal miniplayer | Sessione `/com/lomiri/MediaHub/Service/sessions/0`, ruolo multimedia 2, `IsAudioSource=true`, `IsVideoSource=false`, stato `Playing`. |
| Coda | Sei brani trasferiti; log `Native queue ready: 6 tracks, repeat=Off`. |
| Processo fermato intenzionalmente | PID 8441 in `T (stopped)`; posizione da 43,698 a 53,173 secondi mentre Media Hub resta `Playing`. Processo poi ripreso. |
| Display spento | Luminosità 0, `bl_power=4`, processo in `T`; posizione da 70,881 a 95,297 secondi. |
| Cambio naturale a display spento | Seek agli ultimi 8 secondi del primo brano; il servizio passa da Track/0 a Track/1. Dopo il buffering, secondo brano `Playing` a 18,573 secondi, display sempre spento e app sempre sospesa. |
| Pausa/ripresa nativa a display spento | Comandi D-Bus alla sola sessione Rambutan: `Paused` a 32,467 secondi, poi `Playing` a 32,737. |
| Rientro nell'app | Dopo lo sblocco, processo eseguibile, Track/1 a 82,718 secondi e interfaccia sul secondo brano, “Relaxing Jazz for old souls”, con pulsante pausa. |
| Background con display acceso | Aprendo Impostazioni di sistema, Lomiri porta Rambutan in `T (stopped)`; il servizio resta `Playing`, da 193,644 a 237,549 secondi, luminosità 1710 e `bl_power=0`. |

La luminosità effettiva è stata verificata con `brightness` e `bl_power`:
su questo dispositivo `actual_brightness` restituisce 0 anche a display acceso
e non è stato usato come prova di spegnimento.

Le catture autorizzate mostrano solo Rambutan in primo piano. Non sono state
acquisite schermate di blocco o di Impostazioni. La verifica automatica prova
avanzamento e stato della pipeline; non misura la qualità sonora all'ascolto.

## Test e riproducibilità

Suite finale: **4.240 test, 4.210 superati, 30 esclusi, zero errori**.
Le esclusioni richiedono Windows, FFmpeg x64 o un Secret Service di prova.
La suite comprende 13 nuovi casi Media Hub descritti sotto. Build Release con
`-warnaserror`: zero errori e zero avvisi.

I test `MediaHubAudioPlayerTests` coprono trasferimento completo, salti multipli
durante la sospensione, duplicati in coda, modifiche dei successori, cancellazione
di risoluzioni superate, seek/pausa/ripresa, repeat one/all, protezione dei file
della cache, fine coda, conversione delle unità, recupero dopo riavvio del servizio
e stop/play esterni. Usano un trasporto controllato. `MediaHubConnectionTests`
verifica su un vero bus D-Bus isolato ruolo, unità di seek e distruzione una sola
volta della sessione dalla stessa connessione che l'ha creata. Le prove sul
telefono usano il servizio reale nel Click confinato.

`scripts/device-media-hub-probe.py SESSIONE PID` va eseguito sul telefono con
il numero di sessione indicato nel log di Rambutan e il PID della sua app.
Controlla il nome del processo e restituisce solo stato, posizione, ID traccia,
numero di brani, profilo e stato del display. Non esporta URL firmati, credenziali,
metadati di altri player o schermate.

Le evidenze locali sono in `artifacts/verification/mediahub-*`; log di build,
test e installazione sono conservati accanto a quelle del porting iniziale.
Hash del Click e report della review sono in `artifacts/click/`.

Artefatto finale installato: `rambutan.milattanzio_0.1.1_arm64.click`, SHA-256
`c5b63bb15363531b3eff55dd2a9a8052f493ed8471ff0e8c209812f99865b193`.
Gli hash di App, Core e Integrations sul telefono coincidono con il publish;
il confronto è in `mediahub-final-installed.txt`. Il processo della verifica
finale è 9477, con sessione nativa `/com/lomiri/MediaHub/Service/sessions/1`.

Ripetizione sulla build finale: display spento (`brightness=0`, `bl_power=4`),
cinque brani trasferiti, posizione da 22,016 a 34,456 secondi e processo in `T`.
Dopo un seek vicino alla fine, il servizio passa al successore e lo riproduce
per 11,574 secondi con schermo sempre spento e processo ancora sospeso
(`mediahub-final-off-transition.json`).

Chiusura finale verificata con `SIGTERM`, dopo aver reso eseguibile il processo:
log `Session destroyed`, PID terminato, `UnknownObject` sul precedente path D-Bus
e nessun blocco `request_sys_state`/`keep_display_on` residuo. Evidenza completa
in `mediahub-final-cleanup.txt`. La sessione lasciata dalla prova precedente alla
correzione era stata fermata esplicitamente prima di installare questa build.

## Limiti

- L'audio e i brani già trasferiti proseguono in background. Nuova risoluzione
  YouTube, recupero di URL scaduti ed estensione radio richiedono l'app eseguibile.
  Passare in background prima della preparazione può limitare la coda disponibile.
- Gli URL YouTube hanno scadenza: nessuna garanzia per playlist di molte ore.
  Le transizioni tra stream possono richiedere buffering e non sono gapless.
- Crossfade, equalizzatore e normalizzazione del motore desktop non sono applicati
  da Media Hub; i relativi controlli sono rimossi dalle impostazioni telefoniche.
- Le prove non includono chiamate reali, cuffie Bluetooth, indicatori azionati
  manualmente, metadati della schermata di blocco o misure di autonomia.
- Il telefono era collegato via USB. Display spento e sospensione dell'app sono
  verificati; non si dichiara sospensione profonda del sistema durante l'audio.
- Il validatore Click continua a non conoscere la policy `ubuntu/2404.2` e termina
  con codice 2; non è un'approvazione OpenStore.
