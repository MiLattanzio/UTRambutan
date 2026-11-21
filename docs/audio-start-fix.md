# Correzione avvio audio — 0.1.2

Il messaggio `Media Hub could not start audio (error 4)` è stato ricondotto,
nei casi segnalati sul Fairphone, a risposte HTTP 403 del server degli stream.
Gli URL estratti fallivano anche con un client HTTP separato sullo stesso telefono,
sia senza header aggiuntivi sia con gli header forniti dal resolver. Non era
un problema di permessi AppArmor o di inizializzazione del dispositivo audio.

La risoluzione ora usa `yt-dlp --check-formats` anche insieme a `--print`:
il resolver verifica che il formato selezionato sia effettivamente accessibile,
prima di consegnarlo a Media Hub o alla cache. Con questa modifica, sui due brani
interessati sono state ottenute risposte HTTP 206 sia all'inizio sia oltre il primo
megabyte. Si mantiene la versione stabile già distribuita di yt-dlp; non si
installano nightly o nuove librerie nel telefono.

Il significato dell'opzione è documentato nel
[README ufficiale di yt-dlp](https://github.com/yt-dlp/yt-dlp#video-format-options).
La verifica aggiunge una richiesta di rete alla risoluzione; cache e brani locali
continuano a evitare la nuova risoluzione. Non garantisce la disponibilità futura
di ogni contenuto o degli URL già scaduti.

È stata corretta anche la pulizia dopo un errore di apertura. Il percorso asincrono
del player attende `DisposeAsync` e `DestroySession` senza bloccare il thread che
deve ricevere la risposta D-Bus. La chiusura sincrona rimane per `SIGTERM` e l'uscita
dell'app. Le chiamate che superano il timeout vengono osservate anche quando
falliscono successivamente alla chiusura della connessione: non producono più
eccezioni di task non osservati nel percorso corretto.

Test automatici: **4.241 totali, 4.211 superati, 30 esclusi, zero errori**.
Un nuovo test verifica che l'apertura fallita attenda la pulizia asincrona;
il test sul bus D-Bus isolato verifica anche la distruzione asincrona della sessione.

## Verifica sul Fairphone 5

Click 0.1.2 installato sul dispositivo `49f88e70`; hash degli assembly Streams
e Integrations confrontati con il publish. Profilo AppArmor
`rambutan.milattanzio_rambutan_0.1.2 (enforce)`.

Il brano corrente che aveva fallito nella 0.1.1 è stato riavviato dal miniplayer:
sessione nativa `/com/lomiri/MediaHub/Service/sessions/14`, stato `Playing`, audio
e nessuna sorgente video, avanzamento osservato da 32,298 a 72,462 secondi senza
nuovi errori nel log dell'istanza aggiornata. La coda ha trasferito otto brani,
incluso quello corrente. I dati delle prove e i log aggiornati
sono in `artifacts/verification/audio-fix-*`, esclusi dal repository; gli URL
firmati non vengono esportati dai controlli HTTP.

Build Release con `-warnaserror`: zero errori e avvisi. Click finale SHA-256:
`e64c539c327c99d5964c4a5035d7820b7b86339af12ae824b964734b938f647f`.
Resta il limite già documentato della review Click, il cui database non riconosce
la policy `ubuntu/2404.2`; non è una nuova approvazione OpenStore.
