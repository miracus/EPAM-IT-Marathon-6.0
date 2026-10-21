# Green & Tasty — Group Pre-order · User Stories v3 (US13–US21)

Продовження **US12 — Consolidated Group Pre-order View** (папка `T1`).
Кожна історія прив'язана до екрана прототипу `T2/v3/index.html` і до фрейму у Figma (сторінка «T2 — Group Cart v3»).

| ID | Назва | Екран | Cost | Value |
|---|---|---|---|---|
| US13 | Дедлайн змін і відправка замовлення в ресторан | 01, 06 | Medium | High |
| US14 | Нагадування гостям (вручну й автоматично) | 01, 07 | Low | High |
| US15 | Власник змінює замовлення гостя | 03 | Medium | Medium |
| US16 | Гість бачить зміни, зроблені власником | 04 | Low | Medium |
| US17 | Розділення рахунку і статус оплати | 07 | High | Medium |
| US18 | Коментарі до страв і алергії | 01, 02, 05 | Low | High |
| US19 | Час подачі страви | 02, 05 | Low | Medium |
| US20 | Оновлення в реальному часі | 01 | Medium | Medium |
| US21 | Повторне бронювання тією ж компанією | 09 | Low | Low |

> US12 описує екран, на якому власник лише переглядає замовлення. US15 свідомо змінює це правило: власник отримує право видаляти позиції гостей, а гість (US16) завжди бачить, що саме змінено.

---

# 🇺🇦 Українська версія

## US13 — Дедлайн змін і відправка замовлення
**Як** організатор бронювання, **я хочу** бачити, до котрої години група може змінювати передзамовлення, і відправити його в ресторан, **щоб** кухня отримала остаточний список вчасно.

**Передумови:** бронювання «Reserved», групова сесія активна. Дедлайн (cut-off) налаштовує ресторан, за замовчуванням за 2 години до бронювання.

**Acceptance Criteria:**
1. *Given* сесія активна, *When* власник відкриває кошик, *Then* під рядком прогресу показано «Changes close at 17:00 · in 1h 20m». Час до дедлайну оновлюється щохвилини.
2. *When* власник натискає [ Submit Pre-Order ], *Then* замовлення отримує статус «Sent to restaurant», а вгорі з'являється банер «Sent to the restaurant at HH:MM».
3. Після Submit або після дедлайну всі позиції заблоковані. Степери, кнопки видалення, «Remind» і «⋯» приховані, а біля страв гостей показано іконку замка.
4. Гість, який відкриває посилання після блокування, бачить свої страви лише для перегляду і повідомлення «Order was sent — contact the organizer to change it».
5. Якщо власник не натиснув Submit до дедлайну, замовлення відправляється автоматично в момент дедлайну. Недоступні страви при цьому не відправляються.

## US14 — Нагадування гостям
**Як** організатор, **я хочу** одним кліком нагадати гостям, які ще не обрали страви, і ввімкнути автонагадування, **щоб** не писати кожному окремо.

**Acceptance Criteria:**
1. Якщо є гості без страв, у рядку прогресу показано кнопку «Remind N», де N — кількість таких гостей. Натискання надсилає нагадування всім N і показує тост «Reminder sent to Iryna».
2. У рядку гостя без страв є посилання «Remind», яке надсилає нагадування тільки йому.
3. Перемикач «Auto-remind» біля дедлайну за замовчуванням увімкнений. Він надсилає нагадування за 30 хвилин до дедлайну всім, хто ще нічого не обрав.
4. Одному гостю можна надіслати не більше одного ручного нагадування на 15 хвилин. Повторне натискання показує «Already reminded at 15:20».
5. На етапі оплати (US17) «Remind N» нагадує тим, хто ще не оплатив.

## US15 — Власник змінює замовлення гостя
**Як** організатор, **я хочу** прибрати позицію гостя або самого гостя з групового замовлення, **щоб** виправити помилку (наприклад, 5 однакових салатів) до відправки в ресторан.

**Acceptance Criteria:**
1. У кожному рядку страви гостя є іконка кошика. Натискання прибирає позицію одразу, без діалогу підтвердження.
2. На місці прибраної позиції з'являється рядок «‹страва› removed — Anna will be notified · Undo». Undo повертає позицію з тією ж кількістю.
3. Після зміни в заголовку гостя показано статус «Changed by you», а суми гостя і Grand Total перераховуються.
4. Меню «⋯» гостя містить пункти: «Remind ‹ім'я›», «Ask to change order», «Remove from group». Пункт «Remove from group» прибирає гостя та всі його страви.
5. Після Submit або дедлайну (US13) редагування недоступне.

## US16 — Гість бачить зміни, зроблені власником
**Як** гість групового бронювання, **я хочу** знати, що організатор змінив моє замовлення, **щоб** не прийти з думкою, що мою страву приготують.

**Acceptance Criteria:**
1. *Given* власник прибрав позицію гостя, *When* гість відкриває свій кошик, *Then* угорі показано банер «Oleh removed ‹страва› from your order» зі старою і новою сумою.
2. Прибрана страва лишається у списку закресленою, з тегом «Removed by Oleh», і не входить у суму. Після «OK, got it» вона зникає.
3. Кнопка «Pick another dish» відкриває меню, якщо дедлайн ще не минув.
4. Якщо гостя немає на сайті, він отримує push або email «Your pre-order was changed» з назвою страви і датою бронювання.
5. Якщо гостя прибрали з групи, за посиланням він бачить «You were removed from this group order».

## US17 — Розділення рахунку і статус оплати
**Як** організатор, **я хочу** обрати, як група платить, і бачити, хто вже оплатив, **щоб** не збирати гроші вручну.

**Acceptance Criteria:**
1. Після Submit кнопка змінюється на [ Choose how to pay ] і відкриває екран оплати з трьома режимами: «Each pays own», «I pay for all», «Split equally».
2. У режимі «Each pays own» кожна людина бачить суму лише своїх страв. У режимі «Split equally» Grand Total ділиться на кількість учасників з округленням до центів. Різниця від округлення додається до частки організатора.
3. Кожен рядок учасника показує суму і статус «Paid» або «Pending».
4. [ Send payment links ] надсилає кожному учаснику зі статусом «Pending» посилання на оплату його частки.
5. Футер показує «Paid $X · Pending $Y», а X + Y = Grand Total.

## US18 — Коментарі до страв і алергії
**Як** учасник групи, **я хочу** додати коментар до страви і вказати алергію, **щоб** кухня врахувала це без дзвінків.

**Acceptance Criteria:**
1. Під кожною своєю стравою учасник бачить посилання «+ Add note». Коментар до 80 символів, з лічильником «18/80».
2. Блок «Allergies or dietary needs» дозволяє вказати алергію. Вона показується червоним чипом біля імені учасника в кошику власника.
3. Власник бачить коментар під назвою страви в лапках.
4. У режимі «By dish» коментарі показано під стравою з ім'ям автора, а алергії — червоним банером над списком: «Nut allergy — Maksym. Kitchen will be warned.»
5. Коментарі й алергії передаються в ресторан разом із замовленням (US13).

## US19 — Час подачі страви
**Як** учасник групи, **я хочу** обрати, коли подати страву (одразу або після основних), **щоб** десерт не принесли разом із салатом.

**Acceptance Criteria:**
1. Біля кожної своєї страви учасник бачить селектор «Serve: On arrival ▾» зі значеннями «On arrival» і «After mains».
2. Для десертів значення за замовчуванням — «After mains», для решти страв — «On arrival».
3. У кошику власника значення, відмінне від стандартного, показано синім чипом біля назви страви.
4. У режимі «By dish» страви згруповано у секції «On arrival» і «After mains».

## US20 — Оновлення в реальному часі
**Як** організатор, **я хочу** бачити зміни гостей одразу, без оновлення сторінки, **щоб** завжди мати актуальну суму.

**Acceptance Criteria:**
1. Нова або змінена позиція гостя з'являється в кошику власника не пізніше ніж за 5 секунд, без перезавантаження сторінки.
2. Нова позиція 10 секунд підсвічена зеленим і має тег «NEW». З'являється тост «Anna added Quinoa Bowl».
3. У футері показано «● Live · updated HH:MM». Якщо з'єднання втрачено, індикатор стає сірим з текстом «Offline · reconnecting…».
4. Суми й лічильники («4 of 5 joined · 3 chose dishes», «By dish N») оновлюються разом зі списком.

## US21 — Повторне бронювання тією ж компанією
**Як** організатор, **я хочу** одним кліком забронювати новий столик для тієї ж компанії, **щоб** не збирати людей заново.

**Acceptance Criteria:**
1. Для завершеного (Completed) бронювання кошик показує підсумок: дата, локація, кількість людей, кількість страв, сума.
2. Кнопка [ Book again with same group ] відкриває форму бронювання з тією ж локацією і кількістю гостей. Дату й час потрібно обрати заново.
3. Після створення нового бронювання запрошення автоматично отримують усі, хто брав участь у попередньому.
4. Для скасованого або перенесеного бронювання діє AC9 з US12: деталі замовлення не показуються.

---

# 🇬🇧 English version

## US13 — Change cut-off and sending the order
**As a** reservation owner, **I want to** see until when the group can change the pre-order and send it to the restaurant, **so that** the kitchen receives the final list on time.

**Pre-conditions:** the reservation is "Reserved" and the group session is active. The restaurant sets the cut-off; the default is 2 hours before the reservation.

**Acceptance Criteria:**
1. *Given* an active session, *When* the Owner opens the cart, *Then* "Changes close at 17:00 · in 1h 20m" is shown under the progress row. The countdown updates every minute.
2. *When* the Owner clicks [ Submit Pre-Order ], *Then* the order gets the status "Sent to restaurant" and a banner "Sent to the restaurant at HH:MM" appears at the top.
3. After Submit or after the cut-off, all items are locked. Steppers, remove buttons, "Remind" and "⋯" are hidden, and guest items show a lock icon.
4. A guest opening the link after locking sees their dishes read-only and the message "Order was sent — contact the organizer to change it".
5. If the Owner has not submitted by the cut-off, the order is sent automatically at the cut-off. Unavailable dishes are not sent.

## US14 — Guest reminders
**As an** owner, **I want to** remind every guest who hasn't chosen yet in one click and turn on auto-reminders, **so that** I don't have to message each person separately.

**Acceptance Criteria:**
1. If some guests have no dishes, the progress row shows a "Remind N" button, where N is the number of those guests. Clicking it reminds all N and shows the toast "Reminder sent to Iryna".
2. A guest row with no dishes has a "Remind" link that reminds only that guest.
3. The "Auto-remind" toggle next to the cut-off is on by default. It reminds everyone who still has no dishes 30 minutes before the cut-off.
4. Each guest can get at most one manual reminder per 15 minutes. Clicking again shows "Already reminded at 15:20".
5. At the payment step (US17), "Remind N" reminds the people who haven't paid yet.

## US15 — Owner edits a guest's order
**As an** owner, **I want to** remove a guest's item, or the guest, from the group order, **so that** I can fix mistakes (e.g. 5 identical salads) before the order is sent.

**Acceptance Criteria:**
1. Each guest dish row has a trash icon. Clicking it removes the item immediately, with no confirmation dialog.
2. The removed row is replaced by "‹dish› removed — Anna will be notified · Undo". Undo restores the item with the same quantity.
3. After the change, the guest header shows the status "Changed by you", and the guest subtotal and Grand Total are recalculated.
4. The guest's "⋯" menu contains "Remind ‹name›", "Ask to change order" and "Remove from group". "Remove from group" removes the guest and all their dishes.
5. Editing is not available after Submit or the cut-off (US13).

## US16 — Guest sees changes made by the owner
**As a** guest in a group booking, **I want to** know when the organizer changes my order, **so that** I don't arrive expecting a dish that won't be cooked.

**Acceptance Criteria:**
1. *Given* the Owner removed a guest's item, *When* the guest opens their cart, *Then* a banner at the top reads "Oleh removed ‹dish› from your order" and shows the old and new total.
2. The removed dish stays in the list, struck through and tagged "Removed by Oleh", and is not counted in the total. It disappears after "OK, got it".
3. "Pick another dish" opens the menu if the cut-off hasn't passed.
4. A guest who is not on the site gets a push or email "Your pre-order was changed" with the dish name and the reservation date.
5. A guest removed from the group sees "You were removed from this group order" when opening the link.

## US17 — Split the bill and payment status
**As an** owner, **I want to** choose how the group pays and see who has already paid, **so that** I don't have to collect money manually.

**Acceptance Criteria:**
1. After Submit, the CTA becomes [ Choose how to pay ] and opens the payment screen with three modes: "Each pays own", "I pay for all" and "Split equally".
2. In "Each pays own", each person sees the amount for their own dishes only. In "Split equally", the Grand Total is divided by the number of participants and rounded to cents. Any rounding difference goes to the organizer's share.
3. Each participant row shows the amount and the status "Paid" or "Pending".
4. [ Send payment links ] sends each "Pending" participant a link to pay their share.
5. The footer shows "Paid $X · Pending $Y", where X + Y = Grand Total.

## US18 — Dish notes and allergies
**As a** group member, **I want to** add a note to a dish and state an allergy, **so that** the kitchen takes it into account without phone calls.

**Acceptance Criteria:**
1. Each of the member's own dishes has a "+ Add note" link. A note can be up to 80 characters, with a counter such as "18/80".
2. The "Allergies or dietary needs" block lets the member state an allergy. It is shown as a red chip next to the member's name in the Owner's cart.
3. The Owner sees the note under the dish name, in quotes.
4. In "By dish" view, notes appear under the dish with the author's name, and allergies appear as a red banner above the list: "Nut allergy — Maksym. Kitchen will be warned."
5. Notes and allergies are sent to the restaurant together with the order (US13).

## US19 — Serving time
**As a** group member, **I want to** choose when my dish is served (on arrival or after mains), **so that** dessert doesn't arrive together with the salad.

**Acceptance Criteria:**
1. Each of the member's own dishes has a "Serve: On arrival ▾" selector with the values "On arrival" and "After mains".
2. Desserts default to "After mains"; all other dishes default to "On arrival".
3. In the Owner's cart, a non-default value is shown as a blue chip next to the dish name.
4. In "By dish" view, dishes are grouped into "On arrival" and "After mains" sections.

## US20 — Real-time updates
**As an** owner, **I want to** see guests' changes immediately, without refreshing, **so that** the total is always up to date.

**Acceptance Criteria:**
1. A new or changed guest item appears in the Owner's cart within 5 seconds, without a page reload.
2. A new item is highlighted green with a "NEW" tag for 10 seconds, and the toast "Anna added Quinoa Bowl" appears.
3. The footer shows "● Live · updated HH:MM". When the connection is lost, the indicator turns grey and reads "Offline · reconnecting…".
4. Totals and counters ("4 of 5 joined · 3 chose dishes", "By dish N") update together with the list.

## US21 — Book again with the same group
**As an** owner, **I want to** book a new table for the same group in one click, **so that** I don't have to gather people again.

**Acceptance Criteria:**
1. For a Completed reservation, the cart shows a summary: date, location, number of people, number of dishes and total.
2. [ Book again with same group ] opens the booking form with the same location and guest count. Date and time must be chosen again.
3. Once the new reservation is created, everyone who took part in the previous one automatically gets an invite.
4. For a Cancelled or moved reservation, AC9 of US12 applies: order details are not shown.
