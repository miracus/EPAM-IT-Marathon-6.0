# Backend (.NET) #5: груповий pre-order для Owner. Що зроблено і як перевірити

Короткий підсумок:
- **Код:** рівні 1–3 домашки та розширення US15/US16. Усе злито в `main`: [PR #1](https://github.com/miracus/EPAM-IT-Marathon-6.0/pull/1) і [PR #2](https://github.com/miracus/EPAM-IT-Marathon-6.0/pull/2).
- **Перевірка:** збірка без помилок і попереджень (0 errors / 0 warnings), 191 unit-тест пройдено, E2E на живому API в AWS — 45 з 45.
- **AWS:** бекенд розгорнутий у регіоні `eu-north-1`, API: `https://lntupqp0qf.execute-api.eu-north-1.amazonaws.com/develop`.

---

## 1. Як перевірити, що бекенд розгорнутий

Усі команди запускаються у PowerShell на твоєму комп'ютері, де вже налаштований `aws configure`.

### 1.1. Lambda оновлена (AWS CLI)

```powershell
aws lambda get-function --function-name api-handler --region eu-north-1 `
  --query "Configuration.[LastModified,CodeSize,Runtime]" --output table
```

`LastModified` має збігатися з часом останнього `deploy.ps1`.

### 1.2. Нові маршрути є в API Gateway

```powershell
aws apigateway get-resources --rest-api-id lntupqp0qf --region eu-north-1 --limit 500 `
  --query "items[?contains(path,'guest') || contains(path,'group-reservations') || contains(path,'guest-session-id')].path" --output text
```

У списку мають бути:
- `/guest/groups/{groupReservationId}/sessions`
- `/guest/groups/{groupReservationId}/sessions/{guestSessionId}/dishes/{dishId}`
- `/reservations/{reservationId}/guest-session-id`
- `/group-reservations/{groupReservationId}/submit`
- `/guest/sessions/{guestSessionId}/notices/ack`

### 1.3. Швидкий запит без логіну (видно, чи існує маршрут)

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://lntupqp0qf.execute-api.eu-north-1.amazonaws.com/develop/guest/groups/test/sessions
```

| Відповідь | Що означає |
|---|---|
| `401` | Маршрут є і вимагає логіну. Все правильно |
| `403` з `Missing Authentication Token` | Маршруту немає: розгорнута стара версія |

### 1.4. Повна перевірка

```powershell
cd D:\repos\EPUM\EPAM-IT-Marathon-6.0\T3T4\deploy
.\test-backend.ps1     # збірка + 191 unit-тест, лог test-log-*.txt
.\e2e-preorder.ps1     # 45 перевірок на живому API, лог e2e-log-*.txt
```

`e2e-preorder.ps1` сам створює тестові дані: користувачів `@example.com`, локацію, стіл, страви, бронювання на 5 осіб і 3 гостей. Потім він викликає всі нові endpoint-и й порівнює коди відповідей та суми. Скрипт можна запускати повторно, бо щоразу створюються нові дані.

### 1.5. Логи Lambda (якщо щось падає)

```powershell
aws logs tail /aws/lambda/api-handler --since 15m --region eu-north-1
```

---

## 2. Що зроблено і для чого

### 2.1. Нові endpoint-и (домашка + user stories)

| Endpoint | Хто | Для чого | Рівень |
|---|---|---|---|
| `GET /guest/groups/{groupReservationId}/sessions` | Owner | Усі учасники групи, їхні страви, сума по кожному та загальна сума. Owner завжди першим. Є підсумок по стравах (`summaryByDish`). Недоступні страви показуються, але в суму не входять | 🟢 1 + US12 |
| `GET /reservations/{reservationId}/guest-session-id` | Owner | Повертає id власної owner-сесії, а якщо її ще немає, створює | 🔵 2 |
| `POST /group-reservations/{groupReservationId}/submit` | Owner | Бере тільки submitted-сесії, об'єднує страви по `dishId` і додає їх в order. Order отримує статус `SubmittedByCustomer`, група — `Submitted` | 🔵 2 |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` | Owner | Видаляє гостя з групи й повертає оновлений список і суму | 🟣 3 |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}/dishes/{dishId}` | Owner | Прибирає одну страву з кошика гостя. Гість отримує повідомлення | US15 |
| `POST /guest/sessions/{guestSessionId}/notices/ack` | Guest | Гість підтверджує, що бачив повідомлення про зміни | US16 |

Коди помилок:
- `400` — помилка валідації;
- `401` — немає токена;
- `403` — запит робить не власник бронювання;
- `404` — не знайдено;
- `409` — групу вже підтверджено або закрито.

Документація з прикладами JSON: `project/backend/restaurant-backend-app/docs/ApiRoutes.md`.

### 2.2. Виправлені помилки, знайдені під час тестування

| Де | Проблема | Як виправлено |
|---|---|---|
| `GuestSessionService.IsAuthorizedAsync` | Невалідний токен гостя (не Base64) повертав **500** на всіх гостьових запитах | `Convert.TryFromBase64String`, тепер **401** |
| `report-sender/Mapper.cs` | У звітах губилося прізвище офіціанта: оператор `??` має найнижчий пріоритет | Ім'я та прізвище складаються окремо |
| `CreateFeedbackHandler` | Умова `is not null or Customer` блокувала всіх, у кого є роль | `is not null and not Customer` |
| `GroupReservationServiceTests` | Тест інколи падав: випадкова кількість гостей могла бути 1 | Кількість гостей у тесті зафіксована |
| `deploy.ps1` | `syndicate deploy` не оновлює ресурси, які вже існують, тому на AWS лишалася стара Lambda | Якщо API вже є, використовується `syndicate update` |

### 2.3. Безпека пакетів (PR #2)

| Пакет | Було | Стало | Чому |
|---|---|---|---|
| SixLabors.ImageSharp | 3.1.12 | **видалено** | 6 уразливостей, виправлені лише в платній 4.x. Пакет використовувався тільки для визначення типу картинки, тепер тип визначається за сигнатурою файлу |
| NPOI | 2.7.4 | 2.8.1 | Більше не тягне ImageSharp. Прийнято OSMF EULA (плата лише для комерційного використання з доходом ≥ $10k/рік) |
| System.Security.Cryptography.Xml / Pkcs | 8.0.4 / 8.0.1 | 10.0.13 | Уразливості high |
| MimeKit | 4.13.0 | 4.15.1 | Уразливість moderate |
| AWSSDK.Core (звітні Lambda) | 4.0.0.13 | 4.0.101 | Уразливість low |

Також прибрано всі **40 попереджень компілятора**:
- застарілі методи DynamoDB замінено на `LoadConfig`, `SaveConfig`, `ScanConfig` і `BatchGetConfig`;
- виправлено nullable-попередження;
- QuestPDF: `Text(object)` замінено на `Text(string)`;
- xUnit: тести без параметрів позначено `[Fact]`.

---

## 3. Що змінено в початкових файлах проєкту

### 3.1. Нові файли

| Файл | Призначення |
|---|---|
| `Services/GuestSessions/GroupPreOrderService.cs`, `IGroupPreOrderService.cs` | Уся логіка Owner: snapshot, owner-сесія, submit, видалення гостя або страви |
| `Handlers/GuestSessions/GetGroupSessionsHandler.cs` | `GET .../sessions` |
| `Handlers/GuestSessions/GetOwnerGuestSessionIdHandler.cs` | `GET .../guest-session-id` |
| `Handlers/GuestSessions/SubmitGroupPreOrderHandler.cs` | `POST .../submit` |
| `Handlers/GuestSessions/RemoveGuestDishByOwnerHandler.cs` | `DELETE .../dishes/{dishId}` |
| `Handlers/GuestSessions/AcknowledgeGuestNoticesHandler.cs` | `POST .../notices/ack` |
| `Handlers/GuestSessions/GroupPreOrderResponses.cs` | Одна точка, де винятки перетворюються на коди 400/403/404/409 |
| `Models/GuestSessions/GroupPreOrderModels.cs` | Request/response моделі для L2 і US16 |
| `Entities/GuestNotice.cs` | Повідомлення гостю про зміну його замовлення |
| `Tests/Services/GroupPreOrderServiceTests.cs` | 37 unit-тестів на всі сценарії |
| `Tests/Services/S3ImageServiceTests.cs` | Тести визначення типу картинки |
| `T3T4/deploy/test-backend.ps1` | Збірка й тести з логом у файл |
| `T3T4/deploy/e2e-preorder.ps1` | E2E-перевірка на живому API |

Шляхи до коду наведено відносно `project/backend/restaurant-backend-app/dnapp/lambdas/api-handler/`.

### 3.2. Змінені файли бекенду

| Файл | Що змінено |
|---|---|
| `deployment_resources.json` | Додано 5 нових маршрутів API Gateway: 4 з Cognito-авторизацією, `notices/ack` без неї, бо там перевіряється токен гостя |
| `Routing/RouteRegistry.cs` | Нові маршрути та реєстрація `IGroupPreOrderService` у DI |
| `Handlers/GuestSessions/DeleteGuestSessionHandler.cs` | Переписано під L3: повертає snapshot, використовує новий сервіс |
| `Models/GuestSessions/GetAllResponse.cs` | Додано `summaryByDish`, `hasUnavailableDishes`, `groupStatus`. Поля з умови домашки не змінені |
| `Models/GuestSessions/GetGuestSessionResponse.cs` | Додано `notices` для US16 |
| `Entities/GuestSession.cs` | Нове поле `notices` у DynamoDB |
| `Services/GuestSessions/GuestSessionService.cs`, `IGuestSessionService.cs` | Старе видалення сесії перенесено в новий сервіс. Додано `AcknowledgeNoticesAsync`. Owner-сесія не рахується в ліміт гостей. Виправлено 500 на невалідний токен |
| `Repositories/GuestSessions/GuestSessionRepository.cs` | Лічильник гостей не враховує owner-сесію |
| `Services/S3ImageService.cs` | Визначення типу картинки без ImageSharp |
| `APIHandler.csproj`, `ReportSender.csproj`, `ReportsHandler.csproj` | Оновлення пакетів, `AcceptNPOIOSMFLicense` |
| `docs/ApiRoutes.md` | Опис нових endpoint-ів |
| Інші файли з PR #2 | Лише прибрано попередження компілятора, поведінка не змінилась (крім двох виправлень із 2.2) |

### 3.3. Інфраструктура та деплой (комміт `cb3901b`, до домашки)

| Файл | Що змінено | Чому |
|---|---|---|
| `infra/terraform/variables.tf`, `cloudfront.tf`, `s3.tf` | Додано змінну `use_cloudfront`. CloudFront став необов'язковим, а як запасний варіант фронтенд публікується як S3-сайт | Новий AWS-акаунт не може створити CloudFront до верифікації через AWS Support |
| `.syndicate-config-restaurant-backend-app/*.yml` | У git лежать плейсхолдери. `deploy.ps1` під час деплою локально підставляє account id, регіон `eu-north-1` і шлях до проєкту | Ключі та id акаунта не потрапляють у git. Акаунт обмежений регіоном `eu-north-1` |
| `T3T4/deploy/deploy.ps1`, `cleanup.ps1`, `DEPLOY.md` | Деплой однією командою з перевіркою Free plan; повне видалення ресурсів з AWS | Гарантія, що гроші не спишуться |
| `.gitignore` | Секрети, tfstate, артефакти збірки, логи | Ключі й великі файли не потрапляють у репозиторій |

---

## 4. Коли й що деплоїти

| Змінилося | Команда |
|---|---|
| Код бекенду, маршрути, пакети | `.\deploy.ps1 -Email <пошта> -Only backend`, потім `.\e2e-preorder.ps1` |
| Фронтенд | `.\deploy.ps1 -Email <пошта> -Only frontend` (після бекенду, якщо фронтенд використовує нові endpoint-и) |
| Terraform (S3, Cognito, CloudFront) | `.\deploy.ps1 -Email <пошта> -Only infra` або повний деплой без `-Only` |
| Після верифікації CloudFront | `.\deploy.ps1 -Email <пошта> -CloudFront` |
| Марафон закінчився | `.\cleanup.ps1` видаляє все з AWS |

Порядок роботи:
1. Код.
2. `test-backend.ps1`.
3. Деплой.
4. `e2e-preorder.ps1`.
5. Злиття PR у `main`.
