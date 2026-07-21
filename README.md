# GOAP Designer для Unity 6

Учебная, но близкая к production-уровню GOAP-система для Unity `6000.3.19f1`. В проект входят типизированный Blackboard, A*-планировщик, профили NPC, универсальные действия и сенсоры, резервирование ресурсов, визуальный граф, runtime-отладчик и benchmark-сцены.

## Быстрый запуск

1. Откройте папку `My project` в Unity.
2. Выберите `Tools > GOAP > Build Demo Project`.
3. Выберите `Tools > GOAP > Open Demo Scene`.
4. Нажмите Play.
5. Откройте `Tools > GOAP > Runtime Debugger` и выберите NPC в Hierarchy.
6. Откройте `Tools > GOAP > Planner Graph`, чтобы видеть активную цель и найденный план.

Для создания собственного контента откройте `Tools > GOAP > Content Wizard` или нажмите `Content Wizard` в окне графа.

В демонстрационной сцене пять заранее созданных агентов. Они находятся в Hierarchy и настраиваются через `GoapAgentAuthoring`, без создания NPC из bootstrap-кода.

| NPC | Профиль | Поведение |
| --- | --- | --- |
| Worker | `Worker Profile` | Находит еду, резервирует её и ест |
| Resident | `Resident Profile` | Находит свободную кровать и отдыхает |
| Guard | `Guard Profile` | Берёт оружие и устраняет врага |
| Survivor | `Survivor Profile` | Сначала закрывает голод, затем усталость |
| Lumberjack | `Lumberjack Profile` | Резервирует дерево и добавляет Wood в Inventory |

## Как устроен GOAP

```text
Sensors -> World State -> Goal Selector -> A* Planner -> Plan -> Executors
                    ^                                      |
                    +------------ Replanning --------------+
```

- `Fact` хранит одно известное NPC значение: `Is Hungry`, `Wood Count`, `Enemy Distance` или `Mood`.
- `Goal` задаёт условия активации, желаемое состояние и приоритет.
- `Action` содержит стоимость, предусловия, эффекты и способ выполнения в сцене.
- `Sensor` переносит фактическое состояние сцены в World State агента.
- `Agent Profile` определяет Domain, разрешённые Actions и Goals, начальные Facts, Sensors и настройки поиска.
- `Smart Object` представляет еду, кровать, дерево, врага или другой объект взаимодействия.

Последовательность Actions заранее не задаётся. Планировщик строит её из текущих Facts и перестраивает при изменении мира.

## Типы Facts

| Тип | Условия | Эффекты |
| --- | --- | --- |
| Boolean | `==`, `!=` | Set |
| Integer | `==`, `!=`, `<`, `<=`, `>`, `>=` | Set, Add, Subtract |
| Float | `==`, `!=`, `<`, `<=`, `>`, `>=` | Set, Add, Subtract |
| Enum | `==`, `!=` | Set |

Пример накопления ресурсов:

```text
Precondition: Wood Count < 3
Effect:       Wood Count += 1
Goal:         Wood Count >= 3
```

World State внутри runtime компилируется в индексы, Boolean Facts упаковываются в bitset, а числовые и Enum-значения входят в компактный ключ состояния для A*.

## Визуальный граф

Откройте `Tools > GOAP > Planner Graph`.

### Создание Domain

1. Нажмите `New Domain`.
2. Выберите путь для `.asset`.
3. В панели Library нажмите `+` рядом с Facts, Actions или Goals.
4. Выберите ноду и настройте её в Inspector справа.
5. Нажмите `Validate`, исправьте ошибки и нажмите `Save`.

Правый клик по пустой области открывает создание нод и инструменты раскладки.

### Соединение нод

У Fact-ноды два порта:

- `Conditions` справа отдаёт значение Fact в предусловие Action или условие Goal.
- `Effects` слева принимает изменение от Action.

Чтобы добавить предусловие:

1. Зажмите левую кнопку на порте `Conditions` Fact-ноды.
2. Протяните линию к `Preconditions` нужного Action.
3. Отпустите кнопку.
4. Выберите Action и уточните оператор и значение в Inspector.

Чтобы добавить эффект:

1. Протяните линию от `Effects` Action к `Effects` нужного Fact.
2. В Inspector Action задайте Set, Add или Subtract.

Для Goal протяните Fact в `Activation` или `Desired State`. Удаление выделенной линии удаляет соответствующее условие из данных. Все операции поддерживают Undo/Redo.

### Навигация и организация

- Колесо мыши меняет масштаб.
- Средняя кнопка перемещает камеру.
- Рамка выделяет несколько нод.
- `Ctrl+C` и `Ctrl+V` копируют и дублируют выбранные определения.
- `Sort Graph` раскладывает весь Domain по причинным слоям и уменьшает пересечения связей.
- `Layout > Sort Selection` в контекстном меню раскладывает только выделенные ноды.
- При сортировке исходные Facts остаются слева, затем идут использующие их Actions, изменённые Facts и Goals.
- Сортировку можно отменить через обычный `Ctrl+Z`.
- `Focus` приглушает всё, что не относится к выбранной причинной ветке; щелчок по пустому месту сбрасывает фокус.
- `Details` показывает или скрывает условия и эффекты внутри нод. По умолчанию граф открывается в компактном режиме.
- `Connections` отдельно скрывает Preconditions, Effects и Goal Links.
- Цвета связей: жёлтый — предусловие, зелёный — эффект, фиолетовый — активация Goal, голубой — желаемое состояние Goal.
- Правый клик открывает Align Left, Align Top и распределение по горизонтали или вертикали.
- `Annotations > Group Selection` создаёт сохраняемую группу.
- `Annotations > Note` создаёт сохраняемую заметку.
- Поиск затемняет неподходящие ноды и фильтрует Library.
- Последние Domain отображаются вкладками под основной панелью.
- Minimap помогает перемещаться по большому графу.

Ошибки и предупреждения показываются цветом и меткой непосредственно на ноде. В панели Validation доступны исправления `Create Executor`, `Add Producer` и `Open Sensor`.

В Play Mode оранжевым выделяется выполняемое действие, жёлтым весь план, зелёным активная цель, бирюзовым изменённые Facts.

## Быстрое создание контента

Откройте `Tools > GOAP > Content Wizard` (`Ctrl+Shift+N`). В мастере пять вкладок.

### Agent

1. Перетащите готовый GameObject NPC в `Existing Object` или оставьте поле пустым, чтобы создать нового агента.
2. Перетащите готовый `Agent Profile`. Если профиля ещё нет, назначьте `Domain` и введите имя: мастер создаст Profile рядом с Domain.
3. При необходимости включите `Inventory` и `Stats`.
4. Нажмите `Setup Selected Object` или `Create Agent`.

Мастер сам добавляет `GoapAgentAuthoring`, `GoapAgent`, универсальный исполнитель и профильный сенсор. У нового агента можно включить `Visible Placeholder`, чтобы сразу получить видимую Capsule в сцене.

### Action

1. Назначьте `Domain`, введите имя, стоимость и при необходимости измените автоматически созданный `Executor ID`.
2. В `Preconditions` добавьте Facts, которые должны быть выполнены до запуска действия.
3. В `Effects` добавьте хотя бы одно изменение World State.
4. Выберите `Execution Recipe` и заполните только относящиеся к нему настройки.
5. Проверьте строку `Generated Steps` и нажмите `Create Action`.

Доступные рецепты: `Wait`, `Move To Named Target`, `Smart Object Interaction`, `Gather Resource`, `Consume Inventory`, `Trigger Animation` и `Invoke Event`. Мастер автоматически строит последовательность универсальных Steps. Например, `Gather Resource` создаёт Find, Reserve, Move, Interact, Wait, Consume Target, Inventory Add и Release.

Из графа Action Builder открывается через правый клик по пустому месту и `Create > Action with Wizard`.

Строки условий учитывают тип Fact: Boolean редактируется переключателем, Integer и Float получают числовые сравнения и операции Set/Add/Subtract, Enum показывает список вариантов. Блок `Create and Connect New Fact` создаёт Fact внутри текущего Domain и сразу подключает его в выбранный список.

### Goal

1. Назначьте `Domain`, задайте название и Priority.
2. Добавьте необязательные `Activation Conditions`.
3. Добавьте обязательный `Desired State`.
4. Проверьте `Reachability Preview`.
5. Нажмите `Create Goal`.

`Reachability Preview` показывает Actions, эффекты которых могут произвести каждое желаемое условие. `Missing producer` означает, что для этой части Goal нужно создать или исправить Action.

Из графа Goal Builder открывается через правый клик по пустому месту и `Create > Goal with Wizard`.

### Behaviour Preset

1. Назначьте `Domain`.
2. Выберите `Basic Needs` или `Resource Gathering`.
3. Оставьте включёнными `Agent Profile`, `Scene Agent` и создание объектов мира, если нужен полностью готовый пример.
4. Нажмите `Add Preset`.

`Basic Needs` создаёт связанные Facts, Actions и Goals для голода и усталости, Profile с начальными `Is Hungry = True` и `Is Tired = True`, агента, еду и кровать. После запуска NPC сначала найдёт еду, поест, затем займёт кровать и отдохнёт.

`Resource Gathering` создаёт Facts доступности и количества ресурса, Action с резервированием, перемещением и добавлением предмета в Inventory, Goal, Sensors, профиль и агента с `GoapInventory`. При включённом `Resource Object` мастер создаёт столько расходуемых объектов, сколько указано в `Target Amount`, поэтому Goal сразу достижим. Поля `Smart Object Category` и `Inventory Item ID` заполняются один раз и согласованно используются во всех созданных настройках.

Повторное применение того же пресета не дублирует определения: существующие Facts, Actions и Goals переиспользуются. После создания нажмите `Sort Graph`, чтобы разложить новую ветку.

### Smart Object

1. Перетащите существующий объект сцены в `Existing Object` или оставьте поле пустым для создания примитива.
2. Задайте `Category`, `Capacity` и `Consume On Use`.
3. Нажмите `Setup Selected Object` или `Create Smart Object`.

`Category` должна точно совпадать со значением в шагах `Find Smart Object` и профильном `Smart Object Sensor`.

## Создание NPC через Profile

1. Создайте GameObject или prefab NPC.
2. Добавьте только `GoapAgentAuthoring`.
3. Нажмите `Create Agent Profile` или выберите `Create > GOAP > Agent Profile`.
4. Назначьте Domain.
5. Заполните Actions и Goals. Пустой список означает «использовать весь Domain».
6. Добавьте Initial Facts, например `Is Hungry = True`.
7. Добавьте Sensors в самом Profile.
8. Перетащите Profile в `GoapAgentAuthoring` и нажмите `Apply Profile`.

`GoapAgentAuthoring` автоматически добавляет `GoapAgent`, `GoapBuiltInActionBehaviour` и `GoapProfileSensorBehaviour`. На конкретном NPC можно задать Initial Fact Overrides и Named Targets. Named Target связывает строковый ID из Profile с Transform сцены.

Кнопка `Add Inventory and Stats` добавляет источники данных `GoapInventory` и `GoapStatSource`.

## Универсальные действия

В Action выберите `Execution = Sequence`. Шаги выполняются сверху вниз:

- `Find Smart Object` находит объект категории.
- `Reserve Target` занимает объект или встаёт в FIFO-очередь.
- `Move To Target` идёт к найденному Smart Object или Named Target.
- `Interact` вызывает событие Smart Object.
- `Wait` ждёт заданное время.
- `Consume Target` использует и при необходимости скрывает объект.
- `Release Target` освобождает резервацию.
- `Inventory Add` и `Inventory Remove` меняют ресурсы.
- `Set Fact`, `Add Fact`, `Subtract Fact` меняют World State без двойного применения эффекта.
- `Trigger Animation` вызывает Animator Trigger.
- `Invoke Event` вызывает событие с ID из `GoapActionEventReceiver`.

Предусловия и эффекты Action остаются контрактом планировщика. Steps описывают физическое выполнение этого контракта в сцене.

### Пример Gather Wood

```text
Preconditions:
  Wood Available = True
  Wood Count < 1

Effects:
  Wood Available = False
  Wood Count += 1

Steps:
  Find Smart Object (Wood)
  Reserve Target
  Move To Target
  Wait
  Consume Target
  Inventory Add (Wood, 1)
  Release Target
```

Для нестандартной механики можно наследовать `GoapActionBehaviour` и использовать Custom Executor ID.

## Профильные сенсоры

В `Agent Profile > Sensors` доступны:

- `Smart Object`: наличие или количество доступных объектов категории.
- `Inventory`: количество предметов с Item ID.
- `Distance`: расстояние до Named Target.
- `Proximity`: количество Collider в радиусе с фильтром Layer и Tag.
- `Stat`: значение из `GoapStatSource`, например Health или Stamina.
- `Time`: игровое время со Scale и Offset.
- `Component Property`: поле или property любого Component через имя типа.
- `Constant`: ручное или событийное значение.

Режимы обновления:

- `Every Decision`: перед каждым циклом решения.
- `Interval`: не чаще заданного интервала.
- `Manual`: после `RequestSensor()` или `RequestAllSensors()`.
- `Event`: обновление инициирует UnityEvent или игровой код.

Старые компонентные сенсоры `GoapSmartObjectSensor`, `GoapInventorySensor`, `GoapDistanceSensor`, `GoapTriggerSensor`, `GoapManualSensor` и `GoapBooleanEventSensor` также поддерживаются.

## Smart Objects и очередь

`GoapSmartObject` хранит Category, Capacity, доступность, timeout резервации и `On Interact`.

Если мест нет, `Reserve Target` ставит NPC в FIFO-очередь. Освобождение происходит при успешном завершении, отмене Action, отключении агента или timeout. После освобождения первый ожидающий агент автоматически становится владельцем.

Это предотвращает одновременное использование одной кровати, еды или дерева несколькими NPC.

## Runtime Debugger

Откройте `Tools > GOAP > Runtime Debugger` в Play Mode.

Debugger показывает:

- Profile, Domain, Status, текущие Goal и Action;
- типизированный World State;
- полный план, стоимость, время и число раскрытых состояний;
- все Goals, приоритеты и причины неактивности;
- все Actions, отсутствующие executors и невыполненные предусловия;
- последние 100 решений, перепланирований, ошибок и отмен.

Кнопки:

- `Pause/Resume`: остановить или продолжить decision loop.
- `Step Action`: запустить следующий шаг решения до границы Action.
- `Replan`: обновить сенсоры и перестроить план.
- `Abort Action`: отменить текущий Action с освобождением ресурсов.
- `Capture`: сохранить копию World State в окне.
- `Restore`: восстановить сохранённое состояние и воспроизвести решение.
- `Copy`: скопировать текстовый снимок для отчёта об ошибке.

## Валидация

Редактор проверяет:

- отсутствующие ссылки и дублирующиеся ID;
- Enum без значений или с дубликатами;
- Action без эффектов, executor или Steps;
- неправильный порядок и неполные настройки Steps;
- недопустимые операции для Boolean и Enum;
- противоречащие условия и эффекты;
- Facts вне Domain;
- недостижимые Goals и предусловия без производителя;
- неиспользуемые Facts.

## Производительность

`GoapPlannerSettings` ограничивает:

- `Max Expanded States`;
- `Max Plan Depth`;
- `Max Planning Milliseconds`.

Поиск можно отменить через `CancellationToken`. В Unity Profiler доступны маркеры:

- `GOAP.Plan`;
- `GOAP.Agent.Decision`;
- `GOAP.Agent.Sensors`;
- `GOAP.Benchmark.SpawnAgents`.

Команда `Tools > GOAP > Build Benchmark Scenes` создаёт сцены на 10, 100 и 500 NPC в `Assets/GOAP/Demo/Benchmarks`.

## Тесты

Запуск: `Tools > GOAP > Run Automated Tests` или `Ctrl+Shift+T`.

Edit Mode проверяет A*, стоимость, числовые и Enum Facts, отмену поиска, очередь, валидацию и граф. Play Mode проверяет полный цикл агента, универсальный исполнитель и демонстрацию из пяти профильных NPC.

## Структура

```text
Assets/GOAP/
  Runtime/Core       Facts, Conditions, Domain, World State, validation
  Runtime/Planning   A* planner, plans, cancellation, limits and metrics
  Runtime/Agent      Agent, Profiles, Executors, Sensors, Inventory, Smart Objects
  Runtime/Demo       Authored demo controller, labels, HUD and benchmark runner
  Editor             Planner Graph, inspectors, debugger and project builder
  Demo               Generated domain, profiles, scenes and materials
  Tests              Edit Mode and Play Mode tests
```
