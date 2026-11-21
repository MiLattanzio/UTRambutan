# Avvio dei brani non in cache — 0.1.6

## Modifiche

Su Ubuntu Touch `StreamResolver` prova prima il client `visionos` supportato da
yt-dlp. Richiede audio Opus diretto HTTPS, con la qualità scelta dall'utente e
`--check-formats` ancora attivo. Se il client non offre un formato utilizzabile,
si torna automaticamente alla selezione standard di yt-dlp, compreso il ripiego
AAC. Anche una nuova risoluzione richiesta dopo un errore di riproduzione usa
direttamente il percorso standard. Il desktop mantiene il comportamento precedente.

`RangedFetcher` sovrappone fino a quattro richieste da 1 MiB. I blocchi vengono
scritti in ordine, verificando stato 206, intervallo, lunghezza totale e corpo
effettivo. Risposte corte, sovrabbondanti o incoerenti non diventano cache valida.
La cancellazione interrompe e attende tutti i lettori ancora attivi.

La prova dentro il Click ha inoltre rilevato che il `TMPDIR` ereditato,
`.cache/confined/rambutan.milattanzio`, non era scrivibile con il suo profilo
AppArmor. `UbuntuTouchPlatform.Configure` imposta ora `TMPDIR` su
`<dati-app>/cache/tmp` prima di avviare Avalonia e i processi esterni. Questo
percorso è già consentito dal profilo: nessuna policy o autorizzazione del
sistema viene ampliata. La risoluzione yt-dlp è stata verificata anche eseguendo
il binario dentro lo stesso profilo AppArmor.

La preparazione per Media Hub resta quella della 0.1.5: file completo con titolo,
artista e copertina incorporati; coda e audio sono posseduti dal servizio nativo.
Nessun server HTTP nell'app e nessuna dipendenza dal suo processo per continuare
un brano già consegnato. Il volume rimane quello di sistema e l'interfaccia AXAML
non cambia.

## Misure sul Fairphone 5

Ubuntu Touch 24.04.5, ARM64, 21 novembre 2026. Sonda compilata dagli stessi
progetti Core/Streams della release, con binari precedenti conservati separatamente.
Ogni misura usa una directory nuova, senza audio né copertina nella cache di
prova. La cache dell'app non viene usata né svuotata dalla sonda.

Il tempo è dalla richiesta di risoluzione al file completo pronto per Media Hub:
include yt-dlp, download e metadati; esclude apertura della sessione audio e
latenza fisica degli altoparlanti. La rete e le cache remote non sono controllabili.

| Brano | 0.1.5 | 0.1.6 | Confronto |
| --- | ---: | ---: | --- |
| Nato sbagliato | 10,41–17,86 s (2 prove) | 3,19–4,40 s (3 prove) | Cache locale vuota a ogni prova |
| BESTIE | 11,28 s | 4,15 s | Prima/dopo consecutivi |
| The Pub Song | 10,65 s | 3,86 s | Prima/dopo consecutivi |

Negli ultimi due confronti l'attesa si riduce di circa il 63–64%. Tutti producono
Opus e lo stesso numero di byte del file finale precedente. Per “Nato sbagliato”
la risoluzione della nuova versione impiega 2,46–3,11 s, il download 0,61–1,04 s
e i metadati 0,13–0,26 s. Non sono garanzie di latenza su altre reti o altri brani.

Evidenze: `artifacts/verification/startup-before.txt`,
`startup-mobile-final.txt`, `startup-paired-tracks.txt`.
`startup-after.txt` e `startup-final.txt` documentano misure intermedie del solo
download parallelo, prima dell'aggiornamento effettivo del binario della sonda.

## Verifiche automatiche

Build Release con `-warnaserror`: zero errori e avvisi.
Suite completa: 4.258 test, 4.228 superati, 30 saltati, zero fallimenti.
In seguito alla correzione del percorso temporaneo sono stati ripetuti la build
completa e i 1.742 test App: tutti superati.
I nuovi casi coprono qualità e verifica del client rapido, ripiego standard/AAC,
cancellazione senza ripiego, download concorrenti fuori ordine, limite di
concorrenza, intervalli corrotti, corpi incompleti e arresto dei lettori.

Click SHA-256:
`0a39d8d7416db65457bcae553534b2d42f68b6252232f5f5dc281aff24df652c`.
La review Click resta soggetta al limite preesistente del database che non
riconosce la policy `ubuntu/2404.2`.

## Ripetere la misura

Pubblicare `tools/Rambutan.StartupProbe/Rambutan.StartupProbe.csproj` per
`linux-arm64`, self-contained. Sul dispositivo, con gli strumenti del Click nel
`PATH`, eseguire `Rambutan.StartupProbe TOOLS QUEUE_JSON OUTPUT_ROOT TRACK_INDEX REPEATS`.
Usare una cartella di prova dedicata, non la directory dati dell'app. La sonda
scrive soltanto tempi, titolo, codec e dimensioni, senza stampare URL firmati.

## App installata, metadati e schermo spento

Il Click finale è stato reinstallato sul Fairphone. Gli hash di App e Streams
coincidono con il publish; il processo PID 8323 usa il profilo
`rambutan.milattanzio_rambutan_0.1.6 (enforce)`.

Per la prova nell'interfaccia, le copie cache di “The Pub Song” e “SEGRETO” sono
state spostate temporaneamente in un backup dedicato. Entrambi risultavano
assenti dalla cache prima dell'avvio. Il tocco su Play alle 21:01:47,656 UTC
ha prodotto lo stato nativo Playing alle 21:01:52,048 UTC: circa **4,4 secondi**.
Media Hub, MPRIS, indicatore normale e indicatore greeter riportano titolo,
artista e copertina corretti. `startup-indicator.png` conferma visivamente
“The Pub Song” / “J-AX” con copertina. La coda nativa di cinque brani è pronta
alle 21:01:57,961 UTC, incluso “SEGRETO”, anch'esso inizialmente non in cache.

Il monitor continuo `startup-off-monitor.txt` registra luminosità **0** e app
in **T (stopped)** per tutta la prova. “SEGRETO” avanza da 209,140 a 211,141 s;
il servizio passa autonomamente a “Salsa (Official Video) (feat. Jake La Furia)”
con copertina diversa, poi avanza da 0,224 a 4,226 s. Stato sempre Playing,
nessun intervento del processo sospeso. La schermata viene riaccesa soltanto
dopo questa registrazione. Le copie originali della cache spostate per il test
sono ripristinate; i file audio della coda nativa restano separati e completi.

Altre evidenze: `startup-app-cold.txt`, `startup-cold-selected.txt`,
`startup-device-final.txt` e `startup-final-indicator.png`.

Riferimenti: [opzioni dei client yt-dlp 2026.10.26](https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/README.md#extractor-arguments),
[verifica dei formati](https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/yt_dlp/YoutubeDL.py).
