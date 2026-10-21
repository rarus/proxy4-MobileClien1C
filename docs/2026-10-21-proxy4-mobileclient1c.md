# Подмена адресов для Мобильного клиента 1С — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Прозрачный TCP-прокси для adb-сервера (порт 5037), подменяющий в команде `am broadcast ... com.e1c.mobile.* ...` имя этого ПК на IP (режимы vm/pc/manual), без модификации 1С/MEmu и без root.

**Architecture:** Клиенты adb подключаются к прокси (127.0.0.1:5037), настоящий adb-сервер переносится на 127.0.0.1:5038. Прокси разбирает smart-socket протокол adb (4-hex длина + payload) на байтовом уровне, переписывает только пакеты `shell:`, подходящие под критерий 1С, остальное ретранслирует побайтно.

**Tech Stack:** C# (.NET Framework 4.x, только стандартные сборки), компиляция `csc.exe` (C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe), без внешних пакетов. Тесты — встроенный `selftest` (без фреймворков).

**Spec:** требования в README.md (раздел «Назначение») и дизайн-решение proxy4_MobileClien1C.

## Global Constraints

- Все файлы только в `<корень проекта>\`
- Не модифицировать файлы 1С, MEmu, эмулятор; root на эмуляторе не используется
- Прокси слушает только 127.0.0.1
- Обработка протокола adb — на уровне байт (в команде есть кириллица UTF-8: `/IBNameTestBase`; префикс длины — байтовая длина)
- Поиск adb.exe: `adb.exe` в текущем каталоге → путь из `adb_path` ini → если не найден и backend не отвечает: предупреждение в лог, работа в passthrough невозможна (ошибка в лог, ожидание)
- Имена/порты по умолчанию: listen 127.0.0.1:5037, backend 127.0.0.1:5038, конфиг `proxy4_MobileClien1C.ini` рядом с exe, лог `proxy4_MobileClien1C.log`
- Запуск вручную (`proxy4_MobileClien1C.exe`), остановка: закрытие окна / Ctrl+C / `proxy4_MobileClien1C.exe stop`
- Комментарии в коде не пишутся (стиль проекта), идентификаторы и лог-сообщения на английском

## Review Focus

1. **Кириллица в extra cmd (UTF-8, многобайтная)** — пересчет длины пакета должен идти по байтам, а не по символам; тест в Task 2.
2. **Разные форматы ключей** — `/DEBUGGERURLhttp://host:port` (схема склеена) vs `/WS http://host:port/...` (пробел + схема); тесты обоих в Task 2.
3. **Чужой процесс на 5037** — миграция только при валидном ответе `host:version`; иначе выход с диагностикой без убийства процесса; тест в Task 4.
4. **Двухшаговая сессия клиента** — `host:transport|serial` затем `shell:...`: переписывается второе сообщение, после него — raw relay; тест в Task 5.
5. **Разные хосты у /DEBUGGERURL и /WS** — замена только совпавшего с именем этого ПК; IP-хост и чужое имя не трогаем; тесты в Task 2.

---

### Task 1: Каркас проекта, сборка, selftest-харнесс, git

**Files:**
- Create: `proxy4_MobileClien1C/Program.cs`, `proxy4_MobileClien1C/SelfTest.cs`, `proxy4_MobileClien1C/Config.cs`, `proxy4_MobileClien1C/build.cmd`, `proxy4_MobileClien1C/.gitignore`, `proxy4_MobileClien1C/proxy4_MobileClien1C.ini`

**Interfaces:**
- Produces: `Program.Main(string[] args)` — dispatch: без аргументов → `RunProxy()` (пока заглушка, вывод "not implemented", exit 1); `selftest` → `SelfTest.RunAll()` → печатает `PASS/FAIL <name>`, возвращает 0 если все прошли; `stop` → заглушка.
- Produces: `SelfTest.Register(string name, Func<bool>)`, `SelfTest.RunAll() : int`; `Config.Load(string path) : Config` (поля: ListenPort=5037, BackendPort=5038, Mode="pc", ManualIp="", AdbPath="", LogFile="proxy4_MobileClien1C.log"); Load создает ini с дефолтами при отсутствии.

- [ ] **Step 1: Создать файлы каркаса**

`Program.cs` — dispatch аргументов; `SelfTest.cs` — статический список тестов; `Config.cs` — ini-парсер (секции `[...]` игнорируются, `key=value`, `#` комментарий); `build.cmd`:
```
@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /out:proxy4_MobileClien1C.exe *.cs
```
`.gitignore`: `proxy4_MobileClien1C.exe`, `*.log`, `proxy4_MobileClien1C.ini` (создается при первом запуске — но дефолтный ini коммитим как `proxy4_MobileClien1C.ini.default`).

- [ ] **Step 2: Скомпилировать и проверить**

Run: `.\build.cmd && .\proxy4_MobileClien1C.exe selftest`
Expected: сборка без ошибок; selftest печатает `0 tests` и возвращает 0.

- [ ] **Step 3: git init + первый коммит**

```
git init && git add -A && git commit -m "chore: project skeleton, build, selftest harness"
```

---

### Task 2: RewriteEngine — перехват и подмена адресов (byte-level, TDD)

**Files:**
- Create: `proxy4_MobileClien1C/RewriteEngine.cs`
- Modify: `proxy4_MobileClien1C/SelfTest.cs` (регистрация тестов)

**Interfaces:**
- Produces: `static bool RewriteEngine.Matches(byte[] payload)` — true если payload начинается с `shell` и содержит `:`, и содержит байты `com.e1c.mobile.` и (`/DEBUGGERURL` или `/WS`) (сравнение без учета регистра ASCII);
- Produces: `static byte[] Rewrite(byte[] payload, string replacementHost, string localHostName)` — возвращает новый payload с замененными хостами (или исходный объект, если замен не было).

Правила `Rewrite` (спецификация):
- Ключи обрабатываются независимо. Для `/DEBUGGERURL`: хост после опциональной схемы `http://`/`https://`, склеенной с ключом. Для `/WS`: ключ, затем пробел(ы), затем опциональная схема, затем хост.
- Терминаторы хоста: `:`, `/`, пробел, конец payload.
- Заменяется только хост, равный `localHostName` (ASCII case-insensitive) или `localHostName + "." + что-угодно` (FQDN). Хост-IP (проверка `IPAddress.TryParse`) — не трогать. Чужое имя — не трогать.
- 4-hex префикс длины в `payload` НЕ входит: `Rewrite` получает payload без префикса, вызовущий пересчитывает длину.
- Замена выполняется на байтах; части вне хостов копируются без изменений.

- [ ] **Step 1: Написать падающие тесты (регистрация в SelfTest.RunAll)**

Тест-фикстура — реальная перехваченная команда (байты UTF-8):
```
am broadcast -n com.e1c.mobile.client/com.e1c.mobile.Starter -a com.e1c.mobile.START_CMD --es cmd "ENTERPRISE /IBNameTestBase /O Normal /DEBUG -http -attach /DEBUGGERURLhttp://PC-NAME:1560 /WS http://PC-NAME:8080/MKClient"
```
Тесты (`Rw*`):
1. `Rw_MatchesRealCommand`: Matches = true для строки выше.
2. `Rw_NoMatchWithoutMarkers`: `shell:am start -n com.android.settings/.Settings` → false; `host:devices` → false.
3. `Rw_ReplacesBothHosts`: localHostName="PC-NAME", replacement="192.168.168.168" → оба вхождения заменены, результат содержит `/DEBUGGERURLhttp://192.168.168.168:1560` и `/WS http://192.168.168.168:8080/MKClient`, не содержит `PC-NAME`.
4. `Rw_IpHostUntouched`: команда с `/DEBUGGERURLhttp://10.0.2.2:1560 /WS http://127.0.0.1:8080/x` → возвращен тот же массив (ReferenceEquals или равенство).
5. `Rw_ForeignNameUntouched`: `/DEBUGGERURLhttp://other-pc:1560` при localHostName="PC-NAME" → без изменений.
6. `Rw_DifferentHostsPerKey`: `/DEBUGGERURLhttp://PC-NAME:1560 /WS http://other-pc:8080/a` → заменен только DEBUGGERURL.
7. `Rw_CaseInsensitiveAndFqdn`: `PC-NAME` и `PC-NAME.corp.local` заменяются.
8. `Rw_CyrillicLengthSafety`: payload с `/IBNameTestBase` — длина результата в байтах = старая − (len("PC-NAME")×2) + (len(replacement)×2); строка содержит `TestBase` без искажений.
9. `Rw_WsNoScheme`: `/WS PC-NAME:8080/a` (без схемы) → заменяется.

- [ ] **Step 2: Запустить selftest, убедиться в падении новых тестов**

Run: `.\build.cmd && .\proxy4_MobileClien1C.exe selftest`
Expected: FAIL у всех `Rw_*` (тип/метод не найден — сначала не компилируется: значит сначала минимальные заглушки, возвращающие null/false, затем FAIL).

- [ ] **Step 3: Реализовать RewriteEngine.cs**

Токенизация байтов: скан на ASCII-маркеры `/DEBUGGERURL` и `/WS` (case-insensitive) с проверкой границ слова слева (перед ключом пробел/кавычка/начало). Внутри — извлечение хоста, сравнение, замена через `List<byte>`.

- [ ] **Step 4: Прогнать selftest**

Run: `.\build.cmd && .\proxy4_MobileClien1C.exe selftest`
Expected: все `Rw_*` PASS.

- [ ] **Step 5: Commit**

```
git add -A && git commit -m "feat: byte-level rewrite engine for 1C mobile debug broadcast"
```

---

### Task 3: IpResolver — режимы подстановки IP

**Files:**
- Create: `proxy4_MobileClien1C/IpResolver.cs`
- Modify: `proxy4_MobileClien1C/SelfTest.cs`

**Interfaces:**
- Consumes: `Config` (Mode, ManualIp).
- Produces: `static string IpResolver.Resolve(string mode, string manualIp)` — `vm` → `10.0.2.2`; `manual` → manualIp, если это валидный IP, иначе null; `pc` → IPv4 адаптера: интерфейс Up, не loopback, имя не содержит `VirtualBox|Hyper-V|vEthernet|WSL|Loopback|TAP`, приоритет интерфейсам с непустым списком шлюзов; null если ничего не найдено. Неизвестный режим → null.

- [ ] **Step 1: Тесты**

1. `Ip_VmMode`: Resolve("vm", "") == "10.0.2.2".
2. `Ip_ManualValid`: Resolve("manual", "192.168.1.1") == "192.168.1.1".
3. `Ip_ManualInvalid`: Resolve("manual", "abc") == null; Resolve("manual", "") == null.
4. `Ip_PcModeSmoke`: на этой машине Resolve("pc","") — не null,IPAddress.TryParse = true, значение != "192.168.56.1" и != "172.25.112.1" (виртуальные адаптеры этого ПК).

- [ ] **Step 2: Запустить — FAIL** (`.\build.cmd && .\proxy4_MobileClien1C.exe selftest`)

- [ ] **Step 3: Реализовать через System.Net.NetworkInformation.NetworkInterface**

- [ ] **Step 4: PASS, коммит** `feat: ip resolver with vm/manual/pc modes`

---

### Task 4: SmartSocket-клиент + AdbControl (backend, миграция, переподключение)

**Files:**
- Create: `proxy4_MobileClien1C/SmartSocket.cs`, `proxy4_MobileClien1C/AdbControl.cs`
- Modify: `proxy4_MobileClien1C/SelfTest.cs`

**Interfaces:**
- Produces (`SmartSocket`, работает с любым портом — нужно для тестов с фейк-сервером):
  - `static int QueryVersion(int port)` — `host:version` → int, исключение при не-adb ответе/таймауте (2 c).
  - `static List<string> QueryDevices(int port)` — `host:devices` → список serial (колонка до таба, состояние `device|offline`).
  - `static bool SendConnect(string serial, int port)` — `host:connect <serial>` → OKAY.
  - `static void SendKill(int port)` — `host:kill`.
  - Протокол: `«%04X» + payload` (payload с завершающим `\n`), ответ `OKAY`/`FAIL` + данные.
- Produces (`AdbControl`):
  - `static string FindAdb(string iniAdbPath)` — `adb.exe` в `Environment.CurrentDirectory` → ini путь → null.
  - `static bool StartBackend(int port, string iniAdbPath)` — запустить найденный adb.exe с env `ANDROID_ADB_SERVER_PORT=<port>`, аргумент `start-server`; true если после этого порт отвечает на `host:version` (или уже отвечал).
  - `static List<string> MigrateExistingServer(int listenPort, int backendPort)` — если listenPort свободен → пустой список; иначе QueryVersion(listenPort) (при исключении — прокинуть наверх: чужой процесс), QueryDevices, SendKill, ждать освобождения порта ≤5 c; вернуть список TCP-serial (содержат `:`).
  - `static void ReconnectDevices(List<string> serials, int backendPort)` — SendConnect каждого.

- [ ] **Step 1: Тесты с фейк adb-сервером (поднимается в тесте на порту 15039+rand, эмулирует smart-socket: OKAY, version=31, devices, kill → закрыть листенер)**

1. `Ss_VersionDevicesKill`: фейк-сервер отвечает version `000f` hex `0004000f...` — реализация фейка в тесте: корректно ответить на `host:version` (`OKAY` + 4-hex + `31\n`), на `host:devices` (два serial: `127.0.0.1:21523 device`, `emulator-x offline`), на `host:kill` (OKAY + stop). Assert: QueryVersion=31; QueryDevices вернул оба serial; SendKill дождался освобождения порта.
2. `Ss_NotAdbServer`: TcpListener, отвечающий мусором `HTTP/1.1 400` → QueryVersion бросает исключение.
3. `Ss_Connect`: фейк отвечает OKAY на `host:connect 127.0.0.1:21523` → SendConnect=true (фейк проверяет присланный payload).

- [ ] **Step 2: FAIL**

- [ ] **Step 3: Реализовать SmartSocket.cs (синхронные сокеты) и AdbControl.cs**

- [ ] **Step 4: PASS, коммит** `feat: smart-socket client and adb backend control`

---

### Task 5: ProxyServer — ретрансляция с перехватом

**Files:**
- Create: `proxy4_MobileClien1C/ProxyServer.cs`
- Modify: `proxy4_MobileClien1C/SelfTest.cs`

**Interfaces:**
- Consumes: `RewriteEngine.Matches/Rewrite`, `IpResolver.Resolve`, `Config`.
- Produces: `class ProxyServer` — `void Start(Config cfg, Action<string> log)`, `void Stop()`; событие перезаписи пишется в лог: `REWRITE '<origHost>' -> '<newHost>' (mode=<mode>)`; `REWRITE-SKIP host='<host>' (ip|foreign)` при пропуске по IP/чужому имени.
- Логика на соединение: клиент → прокси → backend. Фаза command: читаются сообщения `%04X`+payload; payload, начинающийся с `shell` и содержащий `:` — кандидат на Rewrite (если Matches); после пересылки shell-сообщения соединение переходит в фазу stream (двусторонний побайтовый relay). Payload с префиксом `host:` — пересылается, фаза command сохраняется. Любой другой payload — пересылка и переход в stream. Backend недоступен при подключении клиента → одна попытка StartBackend, при неудаче — закрыть клиентское соединение с логом ошибки.

- [ ] **Step 1: Интеграционный тест на двух loopback-портах (15037/15038)**

Фейк-backend на 15038 (из Task 4): принимает соединение, читает сообщение, отвечает `OKAY`, для `shell:` с `am broadcast ... /DEBUGGERURLhttp://<HOSTNAME-из-Environment.MachineName>:1560 ...` — сохраняет payload и закрывает поток. Тест:
1. `Px_EndToEndRewrite`: старт ProxyServer с Config{ListenPort=15037, BackendPort=15038, Mode="vm"}; TcpClient на 15037; отправить `host:transport|127.0.0.1:21523` → прочитать `OKAY`; отправить `shell:am broadcast -n com.e1c.mobile.client/... /DEBUGGERURLhttp://<MACHINE_NAME>:1560 /WS http://<MACHINE_NAME>:8080/MKClient`; прочитать `OKAY`; проверить: фейк получил payload с `10.0.2.2` вместо имени, 4-hex длина совпадает с байтовой длиной payload.
2. `Px_PassthroughHostCommand`: отправить только `host:devices` → получен ответ фейка (`OKAY`+список) без изменений.
3. `Px_StreamAfterShell`: после shell-сообщения отправить произвольные байты — фейк получил их дословно (relay не ломает поток).

- [ ] **Step 2: FAIL**

- [ ] **Step 3: Реализовать ProxyServer.cs** (TcpListener + поток на соединение, класс `Conn` с фазовым состоянием)

- [ ] **Step 4: PASS, коммит** `feat: transparent proxy with command-phase interception`

---

### Task 6: Program — CLI, запуск, graceful stop

**Files:**
- Create: `proxy4_MobileClien1C/Log.cs` (консоль+файл, `Append-уровень`)
- Modify: `proxy4_MobileClien1C/Program.cs`

**Interfaces:**
- Consumes: все предыдущие.
- Produces: поведение CLI:
  - без аргументов: Log init → Config.Load (ini рядом с exe) → `MigrateExistingServer(5037,5038)` (при чужом процессе на 5037 — сообщение об ошибке и выход 2) → `StartBackend(5038)` → reconnect TCP-serial → ProxyServer.Start → Console.Title=`proxy4_MobileClien1C (5037->5038, mode=...)` → ожидание: `EventWaitHandle` `Local\proxy4_MobileClien1CStop` / Ctrl+C / закрытие окна. При остановке: ProxyServer.Stop → SendKill(backendPort) → StartBackend(5037) → reconnect serial → лог "stopped, adb server restored on 5037" (все best-effort, каждая ошибка в лог).
  - `stop`: открыть `Local\proxy4_MobileClien1CStop` (existing) и Set; не существует → `proxy4_MobileClien1C is not running`, exit 1.
  - `selftest`: как раньше.
- Запомненные serial хранятся в Program (список с миграции + при Stop).

- [ ] **Step 1: Ручная проверка stop-механизма (тест автоматический ограничен — два процесса)**

`Px_StopSignal`: в одном процессе создать EventWaitHandle как при Run, из "stop"-ветки в том же процессе проверить Set→событие срабатывает; полноценно — ручной шаг интеграции Task 7. Минимальный тест: `proxy4_MobileClien1C.exe stop` при незапущенном прокси печатает not running, exit 1.

- [ ] **Step 2: Реализация Program.RunProxy/HandleStop + Log.cs**

- [ ] **Step 3: Сборка + selftest PASS, коммит** `feat: cli, lifecycle and graceful adb server restore`

---

### Task 7: Интеграция в реальном окружении + README

**Files:**
- Create: `proxy4_MobileClien1C/README.md`
- Modify: `proxy4_MobileClien1C/build.cmd` (если нужны правки)

**Interfaces:**
- Consumes: собранный `proxy4_MobileClien1C.exe`, реальный adb-сервер MEmu (PID на 5037), эмулятор `127.0.0.1:21523`.

- [ ] **Step 1: Реальная миграция**: при запущенном MEmu-сервере запустить `proxy4_MobileClien1C.exe` → в логе: захват serial, kill, backend на 5038, reconnect 21523 → `adb devices` (клиент MEmu) показывает устройство.

- [ ] **Step 2: Реальная перезапись**: `adb shell am broadcast -n com.e1c.mobile.client/com.e1c.mobile.Starter -a com.e1c.mobile.START_CMD --es cmd "ENTERPRISE /IBNameTestBase /DEBUG -http -attach /DEBUGGERURLhttp://<MachineName>:1560 /WS http://<MachineName>:8080/MKClient"` → в логе прокси `REWRITE '<name>' -> '<ip>' (mode=pc)`; проверка: `ibases.v8i` в эмуляторе содержит IP (чтение через adb shell su — только для проверки, не частью решения).

- [ ] **Step 3: Остановка**: `proxy4_MobileClien1C.exe stop` → порт 5037 снова обслуживает сервер, `adb devices` работает, устройства переподключены.

- [ ] **Step 4: README.md**: запуск, остановка, ini-параметры (mode=vm|pc|manual, manual_ip, adb_path, порты), режимы, лог, диагностика (чужой процесс на 5037, adb.exe не найден).

- [ ] **Step 5: Финальный прогон selftest + коммит** `docs: readme; integration verified`
