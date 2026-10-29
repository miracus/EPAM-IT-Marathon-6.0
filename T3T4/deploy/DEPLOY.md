# Деплой Green & Tasty в AWS (T3/T4)

Скрипт `deploy.ps1` виконує все з `project/README.md` за один запуск:

1. **Terraform** створює S3, CloudFront, Cognito, SQS і Scheduler.
2. **Syndicate** розгортає Lambda (.NET 10), API Gateway і DynamoDB.
3. **Angular** збирає фронтенд, заливає його в S3 і оновлює кеш CloudFront.

**Гроші.** Скрипт спочатку перевіряє план акаунта і **зупиняється, якщо акаунт не на Free plan**. На Free plan AWS не може нічого списати, поки ти сам не натиснеш *Upgrade*. Не натискай.

## 1. Встанови інструменти (один раз)

PowerShell від імені звичайного користувача:

```powershell
winget install -e --id Amazon.AWSCLI
winget install -e --id Hashicorp.Terraform
winget install -e --id Microsoft.DotNet.SDK.10
winget install -e --id Python.Python.3.11
```

Node.js **22.x** встанови з https://nodejs.org/en/download (обери версію 22) або через `nvm-windows`.
Після встановлення **закрий і знову відкрий PowerShell** і перевір:

```powershell
aws --version; terraform -version; dotnet --version; py -3.11 --version; node -v
```

## 2. Увійди в AWS (один раз)

```powershell
aws configure
```

- AWS Access Key ID і Secret Access Key — ключі твого IAM-користувача;
- Default region name — `eu-north-1` (регіон твого проєкту, видно в адресі консолі `region=...`);
- Default output format — `json`.

Ключі вводиш тільки тут, у себе на комп'ютері. Нікому їх не надсилай, зокрема Claude.
Після цього файл `T3T4/aws.txt` можна видалити: ключі збережено у `%USERPROFILE%\.aws\credentials`.

## 3. Запусти деплой

```powershell
cd D:\repos\EPUM\EPAM-IT-Marathon-6.0\T3T4\deploy
Set-ExecutionPolicy -Scope Process Bypass
.\deploy.ps1 -Email твій@email.com
```

Перший запуск триває приблизно 15–25 хвилин. Найдовше створюється CloudFront.
Наприкінці скрипт друкує:

```
SITE:  https://dxxxxxxxxxxxx.cloudfront.net
API:   https://xxxx.execute-api.eu-north-1.amazonaws.com/develop
```

Посилання **SITE** і здаєш на рівень 2.

На email прийде лист від AWS SNS «Subscription Confirmation». Підтверди його, щоб отримувати алерти.

### Якщо щось упало

Скрипт зупиниться і покаже шлях до лог-файлу `deploy-log-*.txt`. Надішли цей файл Claude: ключів у ньому немає.
Потім можна перезапустити лише потрібний крок:

```powershell
.\deploy.ps1 -Email твій@email.com -Only backend    # або infra / frontend
```

## 4. Оновлення після змін у коді

- Змінився бекенд: `.\deploy.ps1 -Email ... -Only backend`
- Змінився фронтенд: `.\deploy.ps1 -Email ... -Only frontend`

## 5. Початкові дані і прибирання тестових даних

```powershell
.\seed-data.ps1
```

Скрипт завантажує в DynamoDB 3 локації (9 Abashidze Street, 14 Baratashvili Street, 48 Rustaveli Avenue), 8 страв і 10 столиків на 2/4/6/8 гостей з папки `seed`. Також через API він створює по одному офіціанту на локацію, бо без офіціанта бронювання не створюється. Паролі офіціантів записуються в `seed\waiter-accounts.local.txt`, цей файл не потрапляє в git. Дані марафону виправлено, деталі в коментарі скрипта. Повторний запуск безпечний.

```powershell
.\cleanup-test-data.ps1            # лише показує, що знайдено
.\cleanup-test-data.ps1 -Delete    # видаляє після введення DELETE
```

Скрипт прибирає дані, які лишили `e2e-preorder.ps1` і `l3-test.ps1`. Це локації «E2E street …» і «Frontend L3 street …» разом з їхніми столиками, стравами, офіціантами, бронюваннями, замовленнями, відгуками, груповими бронюваннями й сесіями гостей. Видалене не відновлюється. Тестових користувачів Cognito скрипт не чіпає.

## 6. Видалити все (після здачі марафону)

```powershell
.\cleanup.ps1
```

Скрипт попросить ввести `DELETE` і видалить усе в зворотному порядку, разом із бакетами стану й артефактів.
