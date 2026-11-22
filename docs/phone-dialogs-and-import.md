# Dialoghi e importazione nativa — 0.1.12

## Interfaccia

I dialoghi su Ubuntu Touch seguono dimensioni e posizione dell'area client della
finestra principale. Non dipendono dalla massimizzazione delle finestre transitorie
X11 in Lomiri. Minimi desktop e dimensionamento automatico non possono più
allargare il dialogo oltre lo schermo.

`Controls/DialogFrame.axaml` fornisce titolo e pulsante di chiusura sempre separati
dal contenuto scorrevole. È usato da nuova playlist/rinomina, conferme, scelta
playlist, accesso, profili, testi, scorciatoie e stile overlay. Il campo e i pulsanti
del dialogo testuale restano in alto quando Maliit occupa la parte inferiore.
La chiusura restituisce lo stesso risultato dell'annullamento.

La barra della libreria dispone sorgente e azioni su righe distinte sul telefono.
La pagina di importazione Spotify dispone selezione e anteprima in verticale e
permette di scorrere. I layout restano in AXAML; `PhoneGridExtensions` seleziona le
definizioni AXAML di righe e colonne per il form factor telefonico.

## Scelta dei file

Sul telefono, sia l'importazione M3U/M3U8 sia quella Spotify CSV/ZIP/JSON usano
`Lomiri.Content.ContentPeerPicker` e il provider selezionato, ad esempio Gestore
file. L'integrazione segue il [flusso Content Hub di Ubuntu Touch](https://docs.ubports.com/en/latest/appdev/guides/contenthub.html).
Sul desktop rimane lo StorageProvider Avalonia.

Il componente `rambutan-file-picker` usa Qt 5 Quick e il modulo di sistema
`Lomiri.Content`, senza WebEngine. È incorporato nella finestra di importazione
esistente. Il Click richiede il gruppo AppArmor standard `content_exchange`,
senza accesso generale al filesystem.

Il file trasferito viene copiato in una cartella privata prima di finalizzare
il trasferimento Content Hub. Il risultato viene validato e la copia privata
rimossa al termine della lettura, anche in caso di errore. Annullamento, chiusura
dell'app e timeout terminano il componente. Le credenziali non entrano in questo
protocollo.

Content Hub trasferisce il file playlist selezionato, non i file audio indicati al
suo interno. Per M3U locali servono percorsi assoluti a file già accessibili a
Rambutan; i percorsi relativi vengono rifiutati con un messaggio esplicito invece
di registrare riferimenti a una cartella temporanea.

## Verifiche del 22 novembre 2026

- Compilazione ARM64 Release: zero warning ed errori.
- 82 test mirati: dialoghi, selettore/validazione/cleanup, importazione locale e
  Spotify, accesso browser e integrazione Ubuntu Touch.
- Layout del dialogo verificato anche a 320 × 568 e in orizzontale con inset
  tastiera simulato. Conferme lunghe scorrono mantenendo raggiungibile Annulla.
- Fairphone 5: nuova playlist con tastiera Maliit aperta, campo e pulsanti
  visibili; Annulla e X riportano alla libreria senza creare una playlist.
- Fairphone 5: selettore nativo → Gestore file → Downloads → file M3U8 di prova,
  ritorno a Rambutan e rimozione della copia temporanea.
- Fairphone 5: annullamento del selettore e successiva riapertura; trasferimento
  e lettura del CSV di prova. L'anteprima scorrevole mostra un brano,
  “One More Time” di Daft Punk, con i comandi dentro lo schermo.
  Nessuna playlist è stata creata nell'account. I due file di prova in Downloads
  sono stati rimossi al termine, insieme alle copie temporanee private.

Prove visive e log in `artifacts/verification/dialog-contenthub/`,
`artifacts/verification/dialog-tests.log` e
`artifacts/verification/dialog-arm64-rebuild.log`.

Il pacchetto è `artifacts/click/rambutan.milattanzio_0.1.12_arm64.click`;
lo SHA-256 è in `artifacts/click/SHA256SUMS`. Il controllo locale Click già noto
non riconosce la policy 2404.2: non equivale a una revisione OpenStore superata.
Questa versione viene installata sul telefono di prova; la pubblicazione
OpenStore documentata separatamente resta la 0.1.11.

SHA-256 del Click verificato: `9914f042c64e4794d2a2e19d758adf1044b6490952df7c573f7f93acf1ac9a9c`.
L'assembly installato sul Fairphone coincide con quello pubblicato in
`artifacts/linux-arm64`: `8347a8281ed1ff808d1e0821fa97c529e37c317e37429693de0be05587588ea8`.

La creazione effettiva di playlist nell'account è coperta da test con catalogo
fittizio, non da modifiche ai dati dell'utente. Le prove non certificano ogni
provider Content Hub né l'accesso ai file audio esterni al sandbox.
