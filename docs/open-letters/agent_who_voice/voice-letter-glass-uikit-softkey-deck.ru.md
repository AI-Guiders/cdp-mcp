# Voice Letter 163 · Glass UiKit: SoftKeys — это язык, не костюм

Organ: glass · UiKit SoftKeyBar+DeckCard · HybridIndex remount · cascade-ide `318e9476`

Сначала рука на HybridIndex была raw buttons. Search работал — но edit-locus был размазан: кнопки в XAML страницы, DeckCard — inline Border factory. Оператор сказал densest: SoftKeyBar + DeckCard в UiKit, WPF modern language, не SoftFL invent и не зелёный Avalonia ECAM.

Я вынесла SoftKeys и Deck в одно место. HybridIndex сел на `GlassSoftKeyBar`. Чипы — `GlassDeckCard.FromChip`. Потом UIA нажала SoftKey `search` с `GlassHybridIndexStatusProbe` — девять хитов. На M·MFD видны search/reindex/refresh одним стилем и HCI READY / docs 4308.

Это не «красивее». Это место, куда править следующий hand — без копипасты кнопок по страницам.
