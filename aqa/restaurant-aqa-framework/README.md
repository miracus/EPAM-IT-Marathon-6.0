# ДЗ #8 Test Automation — груповий pre-order

**Напрям 1.** Backend і Frontend реалізовані в попередніх ДЗ, інтегровані й розгорнуті на AWS, тому тестується власний застосунок: UI-тести основних сценаріїв і API-тести авторизації та валідації. Виконано **всі три рівні**: L1 — перегляд учасників, L2 — підтвердження, L3 — видалення учасника, плюс наскрізний сценарій.

**Підхід: BDD + Screenplay**, на наданому фреймворку (Reqnroll/Gherkin + TUnit + Playwright for .NET):
- сценарії написані бізнес-мовою;
- кроки тонкі: лише виконують дії (Tasks) і ставлять запитання (Questions) актору;
- дії й запитання працюють через page objects (`Ui/`) або API-клієнти (`Api/`).

Одне й те саме речення Gherkin у `@ui`-сценарії виконується через інтерфейс, а в `@api`-сценарії через API (`[Scope(Tag = ...)]`). Так обидва шари піраміди описують ту саму поведінку однаковими словами.

## Що покрито

| Фіча | Рівень | UI | API | Що перевіряється |
|---|---|---|---|---|
| `Features/GroupPreOrderReview.feature` | L1 | 5 | 9 | учасники, страви, кількості, сума по кожному; загальна сума та її зміна; «ще обирає» і гість без страв; порожня група; доступ лише для організатора (інший клієнт, офіціант, гість, неавторизований); неіснуюча група |
| `Features/GroupPreOrderSubmit.feature` | L2 | 3 | 11 | надсилання: беруться лише підтверджені кошики, однакові страви об'єднуються; статуси `SubmittedByCustomer` і `Submitted`; офіціант бачить замовлення; кожна умова відмови окремо; повторне надсилання; надсилання не організатором |
| `Features/GroupPreOrderRemoveParticipant.feature` | L3 | 2 | 10 | видалення одного з кількох (сума перерахована); видалення гостя з підтвердженим кошиком; видалення останнього; гість іншої групи (400); неіснуючий (404); не організатор; після надсилання; **наскрізний сценарій** (гості → огляд → видалення → надсилання → офіціант) |

Разом 40 тестів (Scenario Outline розгорнуто): 10 UI і 30 API. Гості приєднуються за запрошенням без облікового запису, тому їхні кошики готуються через API, як дозволяє піраміда з ДЗ.

## Налаштування і запуск

```bash
cd aqa/restaurant-aqa-framework
cp .env.example .env       # UI_URL і API_URL вашого розгортання (API з етапом, напр. …/develop)
dotnet build
dotnet run --no-build -- --report-trx --treenode-filter "/*/*/*/*[Category=group-pre-order]"
```

Для групових сценаріїв потрібні **лише `UI_URL` і `API_URL`**. Кожен сценарій сам реєструє своїх людей і створює свій ресторан:
- організатора та, де потрібно, «іншого клієнта» й «іншого організатора»;
- локацію `AQA-Sample-…`, страви, стіл;
- офіціанта — свіжий обліковий запис, доданий до списку офіціантів цієї локації.

Тому сценарії незалежні й можуть іти паралельно. Облікові записи `WAITER_*` / `ADMIN_*` потрібні лише для решти фіч фреймворку.

**Запуск частинами** (теги Gherkin стають категоріями TUnit):

| Що | Фільтр |
|---|---|
| усі групові сценарії | `--treenode-filter "/*/*/*/*[Category=group-pre-order]"` |
| рівень 1 / 2 / 3 | `[Category=L1]`, `[Category=L2]`, `[Category=L3]` |
| лише UI або лише API | `[Category=ui]`, `[Category=api]` |
| швидкий прогін | `[Category=smoke]` |
| негативні | `[Category=negative]` |
| наскрізний | `[Category=e2e]` |

**Звіти:**
- `TestResults/`: TUnit HTML і TRX;
- `TestResults/reqnroll/reqnroll_report.html`: звіт Reqnroll зі сценаріями й кроками;
- `TestResults/traces/*.zip`: Playwright trace для кожного впалого UI-сценарію; відкрити командою `npx playwright show-trace <файл>`.

**CI:**
- `.github/workflows/aqa-quality.yml` на кожен push і pull request: формат, збірка (попередження = помилки), Gherkin lint, discovery тестів.
- `.github/workflows/aqa-tests.yml` запускається вручну: Actions → AQA Tests → Run workflow, де обирається обсяг (`group-pre-order`, `L1`, `L2`, `L3`, `ui`, `api`, `all`).
  - Потрібні змінні репозиторію `AQA_UI_URL` і `AQA_API_URL`.
  - Впалий тест не зупиняє пайплайн: звіти TUnit і Reqnroll та trace завантажуються як артефакти завжди, а підсумок прогону пишеться в Job summary.
  - Нічний запуск за розкладом прибрано свідомо: кожен прогін створює дані в AWS-акаунті, тому нічого не запускається без людини.

## Тестові дані

Усе, що створює сценарій, має префікс `AQA-Sample-` і видаляється в кінці (`CleanupScope`): бронювання скасовується, а офіціант знімається зі списку. Видаляються також стіл, страви й локація.

Час генерується динамічно: бронювання на 3–4 дні вперед о 12:00 UTC. Так група гарантовано відкрита для змін (вони закриваються за 2 години до візиту), а різні організатори ніколи не перетинаються.

API не має способу видалити:
- облікові записи Cognito: щоразу нові адреси `aqa.sample.<guid>@example.com`;
- сесії гостей та скасовані бронювання.

Ці записи залишаються «сиротами» без локації. Їх прибирає скрипт `T3T4/deploy/cleanup-test-data.ps1`: спершу пробний прогін, потім `-Delete`.

## Поведінка, не визначена в ДЗ (обрана й зафіксована тестами)

| Ситуація | Поведінка |
|---|---|
| Група без учасників | API: `participants: []`, `total: 0`. Сторінка показує лише власну порожню картку організатора «My Pre-Order», повідомлення «Participants haven’t added any dishes yet» і Grand Total $0.00 |
| Організатор як учасник | Його власний pre-order (замовлення бронювання) входить у загальну суму і в консолідоване замовлення. У списку гостей організатора немає |
| Загальна сума | API `total` — сума всіх учасників. **На сторінці Grand Total — те, що піде в ресторан**, тобто лише підтверджені кошики, а непідтверджені позначені «Still choosing» і банером |
| Повторне підтвердження | 409 «Group reservation was submitted.», замовлення не змінюється |
| Підтвердження / перегляд / видалення не організатором | інший клієнт або офіціант: 403 «Only the reservation owner can perform this action.»; гість (токен сесії гостя) або неавторизований: 401, бо відмовляє шлюз |
| Немає жодного підтвердженого кошика | 400 «There are no submitted guest sessions in this group reservation.» |
| У підтверджених кошиках немає страв | 400 «Submitted guest sessions do not contain any dishes.» |
| Замовлення бронювання не знайдено | 400 «Order for reservation {id} was not found.» |
| Бронювання не існує / не цієї групи | 400 «Reservation does not belong to this group reservation.» |
| Групи не існує | 404 «Group reservation was not found»; на сторінці «Group reservation not found» |
| Видалення гостя іншої групи | 400 «Guest session does not belong to this group reservation.» |
| Видалення неіснуючого учасника | 404 «Guest session with id … not found.» |
| Видалення організатором самого себе | 400 «The organizer's own session cannot be removed from the group.» |
| Видалення останнього учасника | дозволено; список стає порожнім, сума 0 |
| Видалення після надсилання | 409 «Group reservation was submitted.» |
| Видалений гість, який уже підтвердив кошик | його страви не потрапляють у надіслане замовлення |

---

# Restaurant AQA framework

End-to-end tests for the Green & Tasty restaurant application: Reqnroll (Gherkin) + TUnit + Playwright for .NET, with the Screenplay pattern.

Guidance for extending the suite (writing Gherkin, steps, Screenplay, test data, working with an AI assistant) is in [CLAUDE.md](CLAUDE.md).

## Run the tests

```bash
cp .env.example .env        # then fill in your own values (see "Configuration")
dotnet build
dotnet run --no-build -- --report-trx                          # everything
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=smoke]"   # by tag
```

Everything a run produces lands in `TestResults/`: the TUnit report and TRX, the Reqnroll report in `reqnroll/`, and Playwright traces in `traces/` (open one with `npx playwright show-trace <file>.zip`).

Some scenarios are expected to fail (`@known-defect`, bugs in the application) or are skipped (`@wip`, no admin account configured). That is intentional.

## Quality gates

| Where | What runs |
|---|---|
| Git hooks (local) | `pre-commit`: `dotnet format` check and Gherkin lint on staged files · `commit-msg`: Conventional Commits · `pre-push`: build with analyzers |
| `aqa-quality.yml` | on every push and pull request: no generated files tracked, format, build (warnings are errors), Gherkin lint, test discovery |
| `aqa-tests.yml` | the scenarios themselves, nightly and on demand (Actions → AQA Tests → Run workflow) |

Enable the hooks once per clone, from the repository root:

```bash
dotnet tool restore --tool-manifest aqa/restaurant-aqa-framework/.config/dotnet-tools.json
git config core.hooksPath aqa/restaurant-aqa-framework/.husky
```

The hooks only act on commits and pushes that touch `aqa/restaurant-aqa-framework`. Gherkin lint needs Node.js.

## Configuration

There is no shared environment and no committed `.env`: copy `.env.example` to `.env` and fill it in with the data of **your own** deployment (see "Testing your own deployment"). `.env` is git-ignored; never commit it.

Settings come from `.env` or real environment variables (variables win):

| Setting | Meaning |
|---|---|
| `UI_URL` | where the frontend is served |
| `API_URL` | base URL of the backend API, including the stage (for example `…/develop`) |
| `WAITER_USERNAME`, `WAITER_PASSWORD` | a waiter account that already exists in the environment under test |
| `TESTTABLE_ID`, `TESTTABLE_WAITER_*` | a table and the waiter assigned to it (only for scenarios that need them) |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD` | an admin account; without it the admin scenarios are skipped |

The full list, with defaults, is in `.env.example`.

## Testing your own deployment

### If you deployed the backend / frontend / DevOps homework

1. `cp .env.example .env` and set `UI_URL` and `API_URL` to your deployment (API URL includes the stage, for example `…/develop`).
2. Waiters cannot be created by signing up, so create a waiter (and, for the admin scenarios, an admin) in *your* environment and put its credentials in `WAITER_*` / `ADMIN_*`. Optionally add a table and its assigned waiter (`TESTTABLE_*`).
3. Run `dotnet build` and a small slice first (`--treenode-filter "/*/*/*/*[Category=smoke]"`).

### If you have not deployed your own homework

If you deployed the original application, test that: set `UI_URL` / `API_URL` as above. Otherwise point the framework at a comparable public site (Booking, Airbnb, OpenTable, …) and write all the layers for it yourself: Gherkin features, step definitions, Screenplay tasks and questions, page objects with locators, and API clients where the site has a usable API. Use only flows you are allowed to automate (no purchases, no real bookings, respect the site's terms), and set `UI_URL` to that site.

1. **Generate a skeleton with Playwright codegen** (see "Presetup instructions"): record one flow, for example search → open a result → check details. Save the generated code to a scratch file, it is only raw material.
2. **Split it across the layers**: locators and small actions go to `Ui/` page objects, the flow becomes a task in `Screenplay/Tasks/`, the read-backs become questions in `Screenplay/Questions/`, the business wording goes to `Features/` and thin glue to `StepDefinitions/`.
3. **Replace weak locators.** Codegen often emits positional or text-fragile selectors. Prefer role + accessible name, label, test id, then CSS (see [CLAUDE.md](CLAUDE.md)); check them in the Playwright inspector.
4. Tag scenarios `@ui` / `@api`, keep them independent, and run `npx gherkin-lint -c .gherkin-lintrc "Features/*.feature"`.

### CI on your own public repository

Publish your work in your own public Git repository (the link is the deliverable). Workflows are disabled in a fresh fork until you enable them. Add the same values under Settings → Secrets and variables → Actions:

- **Variables:** `AQA_UI_URL`, `AQA_API_URL`, `AQA_TESTTABLE_ID`
- **Secrets:** `AQA_WAITER_USERNAME`, `AQA_WAITER_PASSWORD`, `AQA_ADMIN_USERNAME`, `AQA_ADMIN_PASSWORD`, `AQA_TESTTABLE_WAITER_USERNAME`, `AQA_TESTTABLE_WAITER_PASSWORD`

`aqa-quality.yml` runs on every push and pull request; `aqa-tests.yml` (nightly and on demand) tests whatever those variables point at. Never put credentials in the repository, in `.env.example` or in test code.

## Presetup instructions

This framework contains PlaywriteInstaller script so you don't need to setup it manually.
For full BDD highlighting in your IDE you may need to install Reqnroll extension https://docs.reqnroll.net/latest/ide-integrations/index.html
To start the Playwright code generator open Powershell in the framework directory and enter the line:
```bash
bin/Debug/net10.0/playwright.ps1 codegen "<your UI_URL or the public site you test>"
```
Build first so `playwright.ps1` exists. The URL is optional.
