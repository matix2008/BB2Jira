Пишем консольную утилиту для формирования на основе файла экспорта (db-2.0.json) из Bitbucket:
- файла маппинга map.json (запуск с ключем -m)
- файла импорта import.csv (запуск с ключем -c)

Файл import.csv предназначен для импорта задач из Bitbucket в новый проект Jira

# Итоговый формат `map.json`

```json
{
  "kind": {
    "bug": "Bug",
    "task": "Task",
    "enhancement": "Task",
    "proposal": "Task"
  },

  "status": {
    "new": "Backlog",
    "open": "In Development",
    "resolved": "Ready for Release",
    "on hold": "Planned",
    "invalid": "Canceled",
    "duplicate": "Canceled",
    "wontfix": "Canceled"
  },

  "priority": {
    "trivial": "Lowest",
    "minor": "Low",
    "major": "Medium",
    "critical": "High",
    "blocker": "Highest"
  },

  "users": {
    "bitbucket_account_id_or_display_name": {
      "bitbucketDisplayName": "User Name",
      "jiraAccountId": "",
      "jiraEmail": "",
      "jiraDisplayName": "User Name"
    }
  },

  "milestone": {
    "MVP-1": "MVP-1",
    "MVP-2": "MVP-2"
  },

  "version": {
    "1.0": "1.0",
    "1.1": "1.1"
  }
}
```

---

# Правила формирования `map.json` из `db-2.0.json`

## 1. `kind`

Источник:

```text
issues[].kind
```

Правило:

```text
уникальное значение kind → значение Jira Issue Type
```

Дефолтный маппинг:

```json
{
  "bug": "Bug",
  "task": "Task",
  "enhancement": "Task",
  "proposal": "Task"
}
```

Если найдено неизвестное значение:

```text
<bitbucket kind> → Task
```

---

## 2. `status`

Источник:

```text
issues[].status
```

Правило:

```text
уникальное значение status → статус Jira
```

Дефолтный маппинг:

```json
{
  "new": "Backlog",
  "open": "In Development",
  "resolved": "Ready for Release",
  "on hold": "Planned",
  "invalid": "Canceled",
  "duplicate": "Canceled",
  "wontfix": "Canceled"
}
```

Если найдено неизвестное значение:

```text
<bitbucket status> → Backlog
```

---

## 3. `priority`

Источник:

```text
issues[].priority
```

Правило:

```text
уникальное значение priority → приоритет Jira
```

Дефолтный маппинг:

```json
{
  "trivial": "Lowest",
  "minor": "Low",
  "major": "Medium",
  "critical": "High",
  "blocker": "Highest"
}
```

Если найдено неизвестное значение:

```text
<bitbucket priority> → Medium
```

---

## 4. `users`

Источники:

```text
issues[].reporter
issues[].assignee
comments[].user
logs[].user
```

Правило:

```text
уникальный пользователь Bitbucket → пользователь Jira
```

Ключ пользователя выбирать в таком порядке:

```text
account_id
display_name
```

Формат записи:

```json
{
  "557058:xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx": {
    "bitbucketDisplayName": "Ivan Ivanov",
    "jiraAccountId": "",
    "jiraEmail": "",
    "jiraDisplayName": "Ivan Ivanov"
  }
}
```

Значения по умолчанию:

```text
jiraAccountId = ""
jiraEmail = ""
jiraDisplayName = bitbucketDisplayName
```

`jiraAccountId` заполняется руками, если нужно сохранить авторов комментариев и истории.

---

## 5. `milestone`

Источники:

```text
milestones[].name
issues[].milestone
```

Правило:

```text
уникальное значение milestone → значение поля Bitbucket Milestone в Jira
```

Дефолт:

```text
<bitbucket milestone> → <bitbucket milestone>
```

Пример:

```json
{
  "MVP-1": "MVP-1",
  "MVP-2": "MVP-2"
}
```

---

## 6. `version`

Источники:

```text
versions[].name
issues[].version
```

Правило:

```text
уникальное значение version → Fix Version/s в Jira
```

Дефолт:

```text
<bitbucket version> → <bitbucket version>
```

Пример:

```json
{
  "1.0": "1.0",
  "1.1": "1.1"
}
```

---

# Общие правила генерации map.json

1. Пустые значения не добавлять в `map.json`.
2. Все ключи сортировать по алфавиту.
3. Если значение найдено в справочнике `versions` или `milestones`, но не используется в задачах, все равно добавлять его в `map.json`.
4. Если значение используется в задаче, но отсутствует в верхнеуровневом справочнике, все равно добавлять его в `map.json`.
5. При повторной генерации `map.json` не затирать вручную измененные значения, если файл уже существует.
6. Если `map.json` уже существует, утилита должна:

   * добавить новые найденные значения;
   * сохранить существующие ручные правки;
   * не удалять старые значения автоматически.

7. `map.json` используется только для преобразования `db-2.0.json` в `import.csv`.
8. Настройки проекта Jira, имена custom fields, ключ проекта и URL Bitbucket issue в `map.json` не хранить.


# Правила генерации `import.csv` на основе `db-2.0.json` + `map.json`

## 1. Назначение

`import.csv` используется для импорта задач и ошибок из Bitbucket в существующий проект Jira.

Источник:

```text
db-2.0.json
map.json
```

Результат:

```text
import.csv
```

Jira сама формирует новые ключи задач при импорте. Старый номер задачи Bitbucket сохраняется в отдельном поле:

```text
Bitbucket Issue ID
```

---

## 2. Общие правила

1. Одна строка `import.csv` соответствует одной записи из `issues[]`.

2. В CSV включаются только задачи, у которых `issues[].kind` после маппинга преобразуется в:

```text
Task
Bug
```

3. Все преобразования выполняются через `map.json`.

4. Если значение отсутствует в `map.json`, поле оставляется пустым, а ошибка фиксируется в лог.

5. CSV формируется в кодировке:

```text
utf-8-sig
```

6. Разделитель CSV:

```text
,
```

7. Все текстовые значения экранируются стандартными правилами CSV.

8. Строки сортируются по возрастанию:

```text
issues[].id
```

9. Комментарии и история изменений переносятся в Jira как комментарии.

10. Полноценная системная история Jira через CSV не формируется.

---

## 3. Колонки `import.csv`

Минимальный состав колонок:

```csv
Issue Type,Summary,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID,Comment
```

Если у задачи несколько комментариев / записей истории, колонка `Comment` повторяется:

```csv
Issue Type,Summary,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID,Comment,Comment,Comment
```

---

## 4. Правила заполнения колонок

### 4.1. `Issue Type`

Источник:

```text
issues[].kind
map.json.kind
```

Правило:

```text
Issue Type = map.kind[issues[].kind]
```

Пример:

```text
bug → Bug
task → Task
enhancement → Task
proposal → Task
```

---

### 4.2. `Summary`

Источник:

```text
issues[].title
```

Правило:

```text
Summary = issues[].title
```

Если `title` пустой:

```text
Summary = "Bitbucket issue <id>"
```

---

### 4.3. `Description`

Источник:

```text
issues[].content
```

Правило:

```text
Description = issues[].content
```

В конец описания добавляется служебный блок:

```text
---

Imported from Bitbucket
Bitbucket Issue ID: <issues[].id>
```

Если `content` пустой, описание состоит только из служебного блока.

---

### 4.4. `Status`

Источник:

```text
issues[].status
map.json.status
```

Правило:

```text
Status = map.status[issues[].status]
```

Пример:

```text
new → Backlog
open → In Development
resolved → Ready for Release
wontfix → Canceled
```

---

### 4.5. `Priority`

Источник:

```text
issues[].priority
map.json.priority
```

Правило:

```text
Priority = map.priority[issues[].priority]
```

Если приоритет отсутствует:

```text
Priority = Medium
```

---

### 4.6. `Reporter`

Источник:

```text
issues[].reporter
map.json.users
```

Правило:

```text
Reporter = map.users[bitbucket_user_key].jiraAccountId
```

Если `jiraAccountId` пустой, использовать:

```text
jiraEmail
```

Если пользователь не сопоставлен, поле остается пустым, а ошибка фиксируется в лог.

---

### 4.7. `Assignee`

Источник:

```text
issues[].assignee
map.json.users
```

Правило:

```text
Assignee = map.users[bitbucket_user_key].jiraAccountId
```

Если исполнитель отсутствует или не сопоставлен:

```text
Assignee = пусто
```

---

### 4.8. `Created`

Источник:

```text
issues[].created_on
```

Правило:

```text
Created = issues[].created_on
```

Дата приводится к формату:

```text
yyyy-MM-dd HH:mm:ss
```

---

### 4.9. `Updated`

Источник:

```text
issues[].updated_on
```

Правило:

```text
Updated = issues[].updated_on
```

Если `updated_on` отсутствует:

```text
Updated = Created
```

---

### 4.10. `Fix Version/s`

Источник:

```text
issues[].version
map.json.version
```

Правило:

```text
Fix Version/s = map.version[issues[].version]
```

Если версия отсутствует:

```text
Fix Version/s = пусто
```

---

### 4.11. `Bitbucket Milestone`

Источник:

```text
issues[].milestone
map.json.milestone
```

Правило:

```text
Bitbucket Milestone = map.milestone[issues[].milestone]
```

Если milestone отсутствует:

```text
Bitbucket Milestone = пусто
```

---

### 4.12. `Bitbucket Issue ID`

Источник:

```text
issues[].id
```

Правило:

```text
Bitbucket Issue ID = issues[].id
```

---

## 5. Комментарии

Источник:

```text
comments[]
```

Связь с задачей:

```text
comments[].issue = issues[].id
```

Для каждой задачи комментарии сортируются по:

```text
comments[].created_on
```

Каждый комментарий записывается в отдельную колонку:

```text
Comment
```

Формат комментария:

```text
<date>;<jiraUser>;<comment text>
```

Где:

```text
date = comments[].created_on
jiraUser = map.users[comments[].user].jiraAccountId
comment text = comments[].content
```

Если `jiraAccountId` пустой, использовать:

```text
jiraEmail
```

Если пользователь не сопоставлен:

```text
<date>;;[Original Bitbucket author: <display_name>]

<comment text>
```

---

## 6. История изменений

Источник:

```text
logs[]
```

Связь с задачей:

```text
logs[].issue = issues[].id
```

История изменений переносится как служебные комментарии.

Формат комментария истории:

```text
<date>;<jiraUser>;[Bitbucket history] <field>: <changed_from> → <changed_to>
```

Где:

```text
date = logs[].created_on
jiraUser = map.users[logs[].user].jiraAccountId
field = logs[].field
changed_from = logs[].changed_from
changed_to = logs[].changed_to
```

Если пользователь не сопоставлен:

```text
<date>;;[Bitbucket history]
Original Bitbucket author: <display_name>
<field>: <changed_from> → <changed_to>
```

---

## 7. Объединение комментариев и истории

Для каждой задачи формируется единый список событий:

```text
comments[]
logs[]
```

Сортировка:

```text
created_on ASC
```

Каждое событие записывается в отдельную колонку `Comment`.

Типы событий:

```text
comment → обычный комментарий
log → служебный комментарий истории
```

---

## 8. Пустые значения

| Поле                  | Правило                          |
| --------------------- | -------------------------------- |
| `Issue Type`          | пусто + запись в лог             |
| `Summary`             | `Bitbucket issue <id>`           |
| `Description`         | служебный блок импорта           |
| `Status`              | пусто + запись в лог             |
| `Priority`            | `Medium`                         |
| `Reporter`            | пусто + запись в лог             |
| `Assignee`            | пусто                            |
| `Created`             | пусто                            |
| `Updated`             | значение `Created`               |
| `Fix Version/s`       | пусто                            |
| `Bitbucket Milestone` | пусто                            |
| `Bitbucket Issue ID`  | `issues[].id`                    |
| `Comment`             | не создавать для пустого события |

---

## 9. Параметры запуска утилиты

Режим генерации CSV:

```bash
BitbucketToJira.exe -c -i db-2.0.json -m map.json -o import.csv
```

Параметры:

| Параметр | Назначение                   |
| -------- | ---------------------------- |
| `-c`     | режим генерации `import.csv` |
| `-i`     | путь к `db-2.0.json`         |
| `-m`     | путь к `map.json`            |
| `-o`     | путь к `import.csv`          |

---

## 10. Лог генерации

При генерации CSV утилита создает:

```text
import.log
```

В лог записывается:

1. количество обработанных issues;
2. количество экспортированных строк;
3. количество комментариев;
4. количество записей истории;
5. значения, отсутствующие в `map.json`;
6. пользователи без `jiraAccountId` / `jiraEmail`;
7. пустые обязательные поля;
8. пропущенные задачи, если такие есть.

---

## 11. Итоговые файлы

После выполнения должны быть созданы:

```text
import.csv
import.log
```
