# Volume iniziale — 0.1.3

All'apertura di ogni brano Rambutan scriveva `Volume=1` nella sessione Media Hub,
usando la preferenza del player desktop. Nel backend PulseAudio del Fairphone
questa scrittura forza il livello del nuovo flusso al 100%, scavalcando quello
del ruolo multimediale di Ubuntu Touch. L'indicatore rimaneva al valore scelto
dall'utente, finché un comando volume non riallineava il flusso.

È stata rimossa la scrittura automatica da `MediaHubAudioPlayer.PlayAsync`.
Il nuovo flusso eredita il volume di sistema. L'avvio, il cambio brano e il
recupero dopo un riavvio del servizio non riapplicano la preferenza desktop.
Le regolazioni esplicite del volume durante la riproduzione restano disponibili.

## Diagnosi sul Fairphone 5

Una sonda separata ha riprodotto un WAV completamente silenzioso attraverso
Media Hub, con ruolo `multimedia`, confrontando due sessioni e ripristinando
il volume originale prima di distruggerle:

| Apertura | Volume PulseAudio precedente | Volume del nuovo flusso |
| --- | ---: | ---: |
| Senza scrittura di `Volume` | 12818 (19,56%) | 12818 (19,56%) |
| Con `Volume=1` | 12818 (19,56%) | 65536 (100%) |

Il valore dell'indicatore Ayatana era `0.195587158203125`, esattamente
`12818 / 65536`. La proprietà `Volume` di Media Hub non rappresenta il livello
aggiornato dai tasti del telefono: la verifica confronta l'indicatore con il
flusso PulseAudio effettivo, non solo con quella proprietà.

Il percorso nativo è stato verificato nei sorgenti ufficiali
[Media Hub](https://gitlab.com/ubports/development/core/media-hub),
`src/service/gstreamer/playbin.cpp`, dove `set_volume` scrive il volume della
pipeline GStreamer.

## Verifiche automatiche

Suite completa: **4.242 test, 4.212 superati, 30 esclusi, zero errori**.
Il nuovo test riproduce l'inizializzazione del coordinatore con una preferenza
salvata, l'apertura, una regolazione esplicita e un cambio brano; verifica che
solo la regolazione esplicita scriva il volume. Il test di recupero dal riavvio
del servizio verifica anche l'assenza di scritture del volume.

Le evidenze locali sono in `artifacts/verification/volume-fix-*`.

## Verifica dell'app installata

Click 0.1.3 installato sul Fairphone 5 `49f88e70`, con profilo AppArmor in
`enforce`. L'assembly Integrations sul telefono coincide con quello pubblicato:
`738b25a203f3a38cbd690973bdc116c42226020da07087d043ba6e972bc81f07`.

| Prova | Indicatore Ubuntu Touch | Volume PulseAudio di Rambutan |
| --- | ---: | ---: |
| Primo avvio del brano | 19,56% | 12818 / 65536, entrambi i canali |
| Pressione volume giù | 17,56% | 11507 / 65536, entrambi i canali |
| Pressione volume su | 19,56% | 12818 / 65536, entrambi i canali |
| Brano successivo aperto dal pulsante dell'app | 19,56% | 12818 / 65536, entrambi i canali |

I tasti sono stati verificati mediante eventi brevi sui rispettivi dispositivi
evdev, dopo aver controllato le capacità dei tasti. Al termine il livello è
quello originale. La nuova traccia risulta `Playing`, con 47 brani nella coda
nativa; nessun nuovo errore nei log della sessione 18, PID 12969.

Build Release con `-warnaserror`: zero avvisi e zero errori. SHA-256 del Click:
`35e97dcc908d3d70554000a155c6b544e378fefd733e48fdbb9e7b69a39aeecd`.
Il validatore Click mantiene il limite già documentato: non riconosce la policy
`ubuntu/2404.2`; questa verifica non costituisce un'approvazione OpenStore.

## Player telefonico — 0.1.4

Rimossi da `PhoneWindow.axaml` il cursore volume e la relativa etichetta:
la regolazione nell'interfaccia telefonica è affidata ai tasti e all'indicatore
di Ubuntu Touch. È stata eliminata anche la chiave di traduzione non più usata.

Build Release con `-warnaserror` riuscita senza avvisi. I **1.742 test esistenti
dell'app** sono passati, senza errori o esclusioni.

La 0.1.4 è stata installata sul Fairphone 5 e la vista player è stata verificata
visivamente: mostra avanzamento e comandi di riproduzione, senza cursore volume.
Riproduzione ripresa nella sessione Media Hub 19; indicatore e flusso PulseAudio
coincidono a `13069 / 65536` (19,94%). Evidenze: `artifacts/verification/volume-ui-*`.
SHA-256 del Click: `2245ec58da1dee407ae35e5b7e784201b2e2c72270deeb828d52eb93a9228382`.
