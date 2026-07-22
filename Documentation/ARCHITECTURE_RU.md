# Архитектура и технические решения GOAP Designer

Документ описывает не только состав классов, но и причины принятых решений: как данные отделены от исполнения, как выбирается Goal, как A* ищет план, как runtime реагирует на изменения мира и почему визуальный редактор не является источником отдельной логики.

[Вернуться к основному README](../README.md) | [Интеграция](INTEGRATION_RU.md) | [Техно-демо](DEMO_OUTPOST_RU.md)

## 1. Задача системы

Традиционный finite state machine обычно задаёт переходы между состояниями явно. Чем больше потребностей, контекста и альтернатив, тем больше переходов приходится поддерживать вручную.

GOAP задаёт другую модель:

1. агент наблюдает мир и получает World State;
2. выбирает актуальную Goal;
3. ищет последовательность Actions, переводящую World State в Desired State;
4. выполняет Actions в Unity-сцене;
5. проверяет изменения и при необходимости перепланирует.

Контент описывает правила мира, а не готовую последовательность. Например, Goal требует `Enemy Defeated = true`, Attack требует оружие, а Take Weapon его выдаёт. Если оружие уже есть, план содержит только Attack. Если его нет, A* сам строит `Take Weapon -> Attack`.

## 2. Общая схема

```text
                         ScriptableObject data
             +----------------------------------------+
             | Domain -> Facts / Actions / Goals      |
             | Profile -> subset + sensors + settings |
             +--------------------+-------------------+
                                  |
                                  v
Unity scene -> Sensors -> GoapWorldState -> GoalSelector
     ^                                      |
     |                                      v
     |                                A* Planner
     |                                      |
     |                                      v
     +------ Executor <- ActionContext <- GoapPlan
                  |
                  +-> success / failure / cancel
                                  |
                                  v
                            Replanning loop
```

Editor tools редактируют те же ScriptableObject-данные. В player build попадают Runtime assemblies, но не GraphView, inspectors, builders и debugger windows.

## 3. Слои и ответственность

### Runtime/Core

- определения Fact, Condition, Action, Goal и Domain;
- типизированные значения и World State;
- operation/comparison semantics;
- compiled Domain;
- структурная и семантическая валидация;
- layout и annotations как сериализуемые данные редактора без зависимости от GraphView.

### Runtime/Planning

- выбор и оценка Goals;
- A*-поиск;
- compact planner state;
- ограничения, причины отказа, план и метрики.

### Runtime/Agent

- decision loop;
- профили и authoring;
- Sensors и режимы обновления;
- Executors и Action Context;
- Inventory и Stats;
- Smart Objects и резервирование;
- глобальный scheduler планирования;
- debug snapshots и history.

### Editor

- Planner Graph;
- custom inspectors и property drawers;
- Content Wizard и creation service;
- validation UI и quick fixes;
- Runtime Debugger;
- demo/benchmark builders;
- запуск автоматических тестов.

### Demo и TechDemo

Содержат только демонстрационную предметную область. Core не ссылается на Lumberjack, Outpost, монстров или конкретные item ID.

## 4. Data-driven модель

Facts, Actions, Goals, Domain и Profiles являются ScriptableObject assets. Это даёт:

- редактирование без перекомпиляции gameplay-кода;
- общий контент для многих NPC;
- стабильные ссылки Unity serialization;
- возможность custom inspectors и визуального графа;
- diff/merge в Git при `Force Text`;
- разделение определения поведения и состояния экземпляра.

ScriptableObject хранит определение. Изменяемое состояние агента хранится в `GoapWorldState`, текущем плане и runtime-компонентах. Поэтому два агента используют один Action asset, но имеют разные Facts, цели, контекстные targets и прогресс выполнения.

### Идентичность

Каждое определение имеет ID и Display Name. ID используется для стабильного поиска и связи с Executor, а Display Name предназначен для UI. Валидатор проверяет повторяющиеся ID, потому что неоднозначная идентичность разрушает поиск и editor layout.

## 5. Типизированный Blackboard

Первоначальная Boolean-модель проста, но плохо выражает накопление ресурсов и срочность потребностей. Поэтому `GoapValue` поддерживает:

- Boolean;
- Integer;
- Float;
- Enum index.

`GoapFact` задаёт тип, default value и список вариантов Enum. `GoapWorldState` предоставляет типизированные `Get`, `GetInteger`, `GetFloat`, `GetEnumIndex`, `Set` и `SetEnum`.

### Условия

`GoapCondition` объединяет:

- Fact;
- comparison;
- expected value;
- effect operation.

Для проверки Preconditions и Goal State используется `Matches`. Для применения Effects используется `Apply`.

```text
Integer/Float comparison: == != < <= > >=
Boolean/Enum comparison:  == !=

Integer/Float effect: Set, Add, Subtract
Boolean/Enum effect:  Set
```

Одна структура используется в разных местах, но inspector и validator ограничивают операции по контексту. Это сокращает количество сериализуемых типов и сохраняет единое отображение условий.

### Почему target не является Fact

Ссылка на дерево, кровать или врага не входит в planner state. Если хранить Unity Object для каждого варианта цели, пространство A* умножается на количество объектов, а ключ состояния становится зависимым от сцены.

Вместо этого Action описывает `GoapActionTargetDescriptor`. Агент разрешает ближайший подходящий Smart Object или Named Target перед планированием, хранит Transform отдельно и добавляет контекстную стоимость. Planner работает с логическим состоянием, а Executor получает конкретную цель через `GoapActionContext`.

## 6. Domain и Profile

### Domain

Domain является полной моделью одной предметной области:

- список Facts;
- список Actions;
- список Goals;
- позиции нод;
- группы и заметки.

Domain компилируется и переиспользуется несколькими NPC.

### Agent Profile

Profile создаёт роль или архетип поверх Domain:

- разрешённые Actions;
- разрешённые Goals;
- Initial Facts;
- профильные Sensors;
- Decision Interval;
- Goal Switch Threshold;
- Planner Settings;
- diagnostic logging.

Это решает проблему bootstrap-кода. Lumberjack, Guard и Resident отличаются данными Profile, а не разными способами создания GoapAgent.

### Profile Composer

Composer выполняет обратный анализ зависимостей:

1. начинает с Desired State выбранных Goals;
2. ищет Actions, эффекты которых могут установить требуемые условия;
3. добавляет Preconditions выбранных Actions;
4. повторяет процесс до известных исходных Facts;
5. предлагает Sensors или Initial Facts для внешних входов.

Режим `Include Alternatives` выбирает все подходящие производители, иначе композиция предпочитает минимальную стоимость. Замкнутая цепочка без внешнего основания считается неразрешимой.

## 7. Выбор Goal

`GoapGoalSelector` сначала вычисляет `GoapGoalEvaluation` для каждой цели.

Goal доступна, если:

- присутствует Desired State;
- выполнены все Activation Conditions;
- Desired State ещё не выполнен;
- cooldown завершён.

Итоговая оценка:

```text
Final Score = Base Priority
            + sum(Fact Score Modifier)
            + sum(GoapGoalScorerBehaviour)
```

### Fact Score Modifier

Модификатор линейно переводит значение Fact из входного диапазона в диапазон score и ограничивает результат. Он подходит для Hunger, Energy, Health, расстояния и запасов.

### Scene scorer

`GoapGoalScorerBehaviour` является Strategy-компонентом. Он позволяет учитывать данные, которые не стоит добавлять в planner state: приказ игрока, тактическую зону, принадлежность к отряду или временный сюжетный коэффициент.

### Hysteresis

Если новая Goal превосходит текущую меньше чем на `Goal Switch Threshold`, агент сохраняет текущую. Это устраняет oscillation между почти равными нуждами.

### Cooldown

После завершения Goal временно исключается из выбора. Cooldown предотвращает немедленное повторение фоновой цели и даёт другим задачам возможность получить управление.

### Детерминированность

При равном score Goals сортируются по Display Name, затем ID. Такой tie-break делает решение воспроизводимым и упрощает тестирование.

## 8. Планировщик A*

### Состояние поиска

Узел A* содержит:

- компактный `GoapPlannerState`;
- родителя;
- Action, приведшее в состояние;
- накопленную стоимость `g`;
- heuristic `h`;
- глубину;
- sequence number для стабильного tie-break.

Оценка очереди:

```text
f(n) = g(n) + h(n)
```

Open set реализован собственным `MinHeap<SearchNode>`, а `bestKnownCost` хранит минимальную известную стоимость каждого состояния.

### Переход

Для каждого извлечённого состояния:

1. проверяется Desired State;
2. перебираются доступные Actions;
3. Action пропускается, если Preconditions не выполнены;
4. Effects создают следующее planner state;
5. base и dynamic cost прибавляются к `g`;
6. более дорогой уже известный вариант отбрасывается;
7. новый узел помещается в heap.

Если Goal найдена, план восстанавливается проходом по Parent и разворачивается в прямой порядок.

### Эвристика

Для каждого невыполненного условия Goal вычисляется стоимость самого дешёвого прямого Action-производителя. `h` берёт максимум этих значений.

```text
h(state) = max(cheapest direct producer for each unsatisfied goal condition)
```

Эвристика игнорирует предусловия производителей и поэтому не завышает нижнюю границу. Это admissible heuristic: A* сохраняет оптимальность по заданной стоимости.

Сумма стоимостей производителей могла бы завышать оценку, если одно Action закрывает несколько условий, поэтому используется максимум.

### Ранний отказ

До поиска планировщик проверяет, имеет ли каждое невыполненное Goal condition хотя бы одного производителя. При отсутствии возвращается `GoalHasNoProducer`, а не общий `NoPlanFound`.

### Ограничения

Каждая итерация проверяет:

- `CancellationToken`;
- `Max Planning Milliseconds`;
- `Max Expanded States`;
- `Max Plan Depth`.

Причина остановки входит в `GoapPlanResult` и отображается в Runtime Debugger.

### Динамическая стоимость

Перед поиском агент готовит planning context каждого Action. Стоимость может включать:

```text
Action.Cost
+ Distance * DistanceCostPerUnit
+ sum(GoapActionCostProviderBehaviour)
```

Action с недоступным target получает невалидную стоимость и исключается из поиска. Стоимость фиксируется на один запуск A*, чтобы состояние очереди оставалось согласованным.

## 9. Compiled Domain и память

Публичный `GoapWorldState` остаётся удобным типизированным API для Sensors, Executors и игрового кода. Перед A* данные переводятся в оптимизированное внутреннее представление.

`GoapCompiledDomain`:

- присваивает Fact числовые индексы;
- создаёт общий planner layout;
- компилирует Preconditions, Effects и Desired State в обращения по slot;
- кэширует compiled Actions и Goals;
- пересобирает конкретную запись при изменении signature;
- создаёт индексированный default World State.

Boolean Facts упаковываются в `ulong` bitsets. Integer, Float и Enum входят в компактное индексированное состояние и его hash/equality. Один Domain переиспользует layout между всеми агентами.

Это уменьшает Dictionary lookup и количество временных объектов во внутреннем цикле A*. Внешний API при этом не раскрывает оптимизированное хранение.

## 10. Runtime-цикл агента

`GoapAgent` координирует подсистемы, но не содержит правил конкретной игры.

Типичный decision tick:

1. Sensors обновляют World State;
2. Goal Selector оценивает все Goals;
3. проверяется возможность сохранить текущую Goal с учётом hysteresis;
4. при смене или отсутствии плана запрашивается planning slot;
5. разрешаются контекстные targets и costs;
6. A* строит план;
7. агент выбирает Executor первого Action;
8. Executor проходит `EvaluateStart` и запускается;
9. агент контролирует `CanContinue`;
10. success применяет staged и декларативные Effects;
11. failure/cancel записывает причину и вызывает перепланирование;
12. завершённая Goal получает cooldown.

### Причины перепланирования

- выбранная Goal изменилась;
- Action успешно завершено;
- Executor сообщил failure;
- Preconditions или continuation стали невалидны;
- Sensor или gameplay event вызвал `ForceReplan`;
- пользователь нажал Force Replan/Abort;
- выбранная цель сцены исчезла или стала недоступна.

## 11. Исполнение Action

План является декларативным предположением. Executor превращает его в реальное игровое действие.

### Контракт Executor

`GoapActionBehaviour` предоставляет:

- `Supports(action)`;
- `EvaluateStart(context)`;
- `CanStart(context)`;
- `CanContinue(context)`;
- coroutine `Perform(context)`;
- `OnCancelled(context)`;
- состояния Idle, Running, Succeeded, Failed, Cancelled;
- `LastFailureReason`.

`EvaluateStart` должен быть side-effect free. Это позволяет отладчику объяснить доступность, не резервируя объект и не меняя сцену.

`Perform` обязан закончиться через `Succeed` или `Fail`. Если coroutine завершилась без результата, base class фиксирует failure.

### Action Context

`GoapActionContext` содержит Agent, Definition, World State и разрешённую цель. Он также поддерживает staged Facts.

Staging решает проблему частичного изменения логики. Executor может подготовить значения во время выполнения, а применить их только после успеха. `MarkEffectHandled` предотвращает двойное применение, когда Executor сам вычислил эффект, а Action также содержит декларативное изменение этого Fact.

### Built-in executor как Interpreter

`GoapBuiltInActionBehaviour` интерпретирует сериализованный список `GoapActionStep`. Это вариант паттерна Command/Interpreter: каждый step является небольшой командой, а один runner обеспечивает общий lifecycle, диагностику и cleanup.

Такой подход позволяет собирать большинство Actions в Inspector без разрастания enum конкретной игры и без класса на каждую операцию.

### Policies прерывания

`Immediate`, `FinishCurrentAction`, `FinishCurrentPlan` отделяют срочность Goal от транзакционной безопасности Action. Например, угроза может иметь высокий score, но доставка ресурса должна закончить безопасную операцию перед переключением.

## 12. Sensors и реактивность

Sensors реализуют адаптер между произвольным Unity API и типизированным World State.

### Режимы обновления

- `EveryDecision` для дешёвого актуального наблюдения;
- `Interval` для медленно меняющихся величин;
- `Manual` для контролируемых систем;
- `Event` для событийного обновления.

`RequestRefresh()` помечает sensor для следующего чтения. Это позволяет разделить уведомление об изменении и сам перенос данных в World State.

### Profile sensors

`GoapProfileSensorBehaviour` интерпретирует сериализованные definitions из Profile. Поддержаны SmartObject, Inventory, Distance, Proximity, Stat, Time, ComponentProperty и Constant.

### Component sensors

Для явной интеграции доступны отдельные behaviours: distance, inventory, smart object, trigger, boolean event и manual. Проект может наследовать `GoapSensorBehaviour` и избежать reflection.

### Практический компромисс

Polling проще и предсказуемее, event-driven обновление экономнее. Поддержка обоих вариантов позволяет выбирать по частоте изменения данных, а не принуждает всю игру к одной модели.

## 13. Smart Objects и multi-agent доступ

`GoapSmartObject` является реестром доступных объектов взаимодействия и локальным менеджером резервирования.

Он хранит:

- Category;
- Capacity;
- availability;
- reservation timeout;
- владельцев;
- FIFO queue с timeout;
- consume-on-use;
- UnityEvent взаимодействия.

### Жизненный цикл

```text
FindClosest -> RequestReservation -> Move/Interact -> Release
                         |
                         +-> Queue -> Promote when capacity is free
```

Expired reservation удаляется при следующей проверке. Disable объекта очищает владельцев и очередь. Отмена встроенного Action освобождает резервирование автоматически.

### Почему резервирование находится вне World State

Владение является временной multi-agent координацией, а не долгосрочным логическим состоянием отдельного NPC. Хранение reservation owner как Fact сделало бы локальные планы зависимыми от identity всех агентов. Smart Object локализует эту конкуренцию.

### Ограничение

Реестр работает в рамках одного Unity process и не является сетевой authority. В multiplayer правила владения должны быть перенесены на сервер, а GOAP Sensor/Executor должны обращаться к сетевому API.

## 14. Визуальный Graph 2.0

Planner Graph использует `UnityEditor.Experimental.GraphView`, но зависимость ограничена Editor assembly.

### Единый источник истины

Edge не хранит дублирующую логику. Создание связи изменяет Preconditions, Effects, Activation Conditions или Desired State ScriptableObject. Удаление связи удаляет условие. После reload граф восстанавливается из Domain.

Это предотвращает рассинхронизацию графа и runtime.

### Причинная модель связей

- Fact Conditions -> Action Preconditions;
- Action Effects -> Fact Effects;
- Fact Conditions -> Goal Activation;
- Fact Conditions -> Goal Desired State.

Причинные Action-to-Action связи вычисляются через общий Fact, а не редактируются вручную. Поэтому пользователь описывает семантику, а не декоративную стрелку.

### Layout engine

Сортировка строит причинные слои и размещает исходные Facts, потребляющие Actions, изменяемые Facts и Goals. Затем применяется упорядочивание, уменьшающее пересечение edges. Отдельный режим работает только с selection.

Ручные positions, groups и notes сохраняются в Domain. Sort и перемещения записываются через Undo.

### Управление сложностью

- compact/details modes;
- Focus на выбранной ветке;
- фильтры типов connections;
- search dimming;
- groups и notes;
- tabs нескольких Domain;
- Minimap;
- align/distribute;
- Frame All и Frame Plan.

### Runtime overlay

Graph получает выбранный Agent или historical snapshot через отдельный debug context. Data assets при этом не меняются. Overlay помечает selected Goal, running и planned Actions, blocked reasons и Facts, отличающиеся от default.

### Риск GraphView

Unity помечает GraphView как experimental. Поэтому:

- Runtime не ссылается на GraphView;
- layout data использует собственные serializable structs;
- content creation находится в отдельном service;
- замена GraphView потребует переписать представление, но не Domain, Planner или Agent.

## 15. Content Wizard

Wizard является application layer над creation service. Он не реализует отдельные правила GOAP, а атомарно создаёт и связывает существующие assets/components.

Основные задачи:

- setup агента;
- composition профиля;
- создание источника Fact;
- Action recipes;
- Goal builder;
- BasicNeeds и ResourceGathering presets;
- Smart Object authoring.

Все изменения используют Unity Undo и помечают assets dirty. Сервис повторно использует существующее определение по имени, что делает presets пригодными для безопасного повторного запуска.

## 16. Runtime Debugger

Главный принцип debugger: отвечать не только на вопрос «что делает NPC», но и «почему».

### Диагностическая модель

Агент формирует:

- текущее состояние Facts;
- Goal evaluations и отдельные score terms;
- Action diagnostics;
- plan cost, planning time, expanded states;
- planning failure code/message;
- status и failure reason Executor;
- события и decision snapshots.

### Action diagnostics

Для каждого Action показываются:

- наличие Executor;
- base и planning cost;
- context target;
- состояние Preconditions с actual value;
- результат `EvaluateStart`;
- machine-readable issue code;
- человекочитаемая причина.

### History и snapshots

Snapshots создаются на ключевых границах решения и хранят выбранную Goal, план, состояние, оценки и причину. Исторический снимок можно открыть в Graph.

`Restore & Replan` восстанавливает только World State. Это сознательное ограничение: debugger не владеет Transform, Inventory, animation state, reservation и произвольными gameplay-компонентами.

## 17. Валидация

`GoapDomainValidator` выполняет несколько уровней проверок.

### Структура

- null references;
- duplicate ID;
- Fact вне Domain;
- пустые и повторяющиеся Enum options.

### Семантика

- несовместимые comparison/effect operation;
- дублирующиеся или конфликтующие условия;
- Action без Effects;
- Goal без Desired State;
- некорректный target descriptor;
- distance cost без resolvable target.

### Исполнение

- Custom Action без Executor ID;
- неполная built-in configuration;
- неправильный порядок Sequence;
- step без Category, item, Fact или event ID;
- numeric step для ненумерического Fact.

### Достижимость

- Desired Fact без производителя;
- Preconditions без производителя или внешнего источника;
- недостижимая Goal;
- dependency cycle без grounded input;
- неиспользуемый Fact.

Warning не всегда является ошибкой. Например, Enemy Visible закономерно не имеет Action-производителя, если его обновляет Sensor. Profile-aware quick fix предлагает открыть Sensor или задать Initial Fact.

## 18. Планирующий scheduler

Если сотни NPC одновременно получают событие, последовательный вызов всех A* в одном кадре создаёт spike. `GoapPlanningScheduler` ограничивает:

- количество выданных planning slots за кадр;
- суммарное измеренное planning time за кадр.

Необслуженный агент сохраняется в очереди по ID и повторяет запрос на следующем decision tick. Отмена/disable удаляют заявку. Scheduler собирает completed/failed/deferred counts, queue length, total и peak time.

Это cooperative scheduling на главном потоке, а не multithreading. Его задача - распределить нагрузку, не усложняя доступ к Unity objects.

## 19. Benchmark и профилирование

Builder создаёт сцены на 10, 100 и 500 агентов.

Режимы:

- `Visual`: создаёт видимых NPC и цветом показывает idle, queued, active и completed;
- `LogicOnly`: отключает renderers для измерения GOAP без стоимости отображения.

Dashboard показывает:

- avg, median, p95 и max planning milliseconds;
- average и max expanded states;
- frame time и FPS;
- completed, failed, queued и deferred planning requests;
- peak planning time per frame.

Profiler markers:

```text
GOAP.Plan
GOAP.Plan.CompileState
GOAP.Plan.Search
GOAP.Agent.Decision
GOAP.Agent.Sensors
GOAP.Scheduler.BudgetCheck
GOAP.Benchmark.SpawnAgents
```

Разделение Visual/LogicOnly важно: иначе стоимость Renderer, GUI и большого Hierarchy ошибочно приписывается планировщику.

## 20. Применённые паттерны

### ScriptableObject data-driven design

Определения поведения являются assets, а не условиями в Agent-классе. Это упрощает reuse, authoring и diff.

### Strategy

`GoapSensorBehaviour`, `GoapActionBehaviour`, `GoapGoalScorerBehaviour` и `GoapActionCostProviderBehaviour` заменяются пользовательскими реализациями без изменения Planner.

### Template Method

Base Action Behaviour фиксирует lifecycle и status transitions, а наследник реализует `Perform` и optional cancellation.

### Command/Interpreter

`GoapActionStep` хранит команду, а built-in executor интерпретирует sequence.

### Adapter

Sensors адаптируют Inventory, Stats, Physics, time и arbitrary component property к `GoapWorldState`.

### Facade/Authoring component

`GoapAgentAuthoring` является простой точкой настройки NPC и скрывает обязательные runtime-компоненты.

### Repository/Registry

Smart Objects поддерживают runtime registry по Category для поиска без сцепления Planner с конкретными MonoBehaviour.

### Snapshot

Decision snapshot фиксирует логическое состояние и диагностику момента для history и воспроизведения World State.

### Dependency inversion

Planner зависит от декларативных definitions и cost delegate, а не от NavMesh, Inventory, Combat или конкретной сцены.

## 21. Инварианты системы

Для корректного расширения важно сохранять следующие правила:

1. Domain assets являются определениями и не хранят состояние экземпляра.
2. Planner не вызывает Unity gameplay side effects.
3. `EvaluateStart` не меняет сцену.
4. Executor сообщает явный success/failure/cancel.
5. Effects применяются только после success.
6. Target context хранится отдельно от planner state.
7. Cancel освобождает reservation и внешние locks.
8. Внешний Fact имеет Sensor или осмысленный Initial Fact.
9. Dynamic cost не меняется внутри одного A*-поиска.
10. Editor changes модифицируют Domain, а не отдельную копию графа.

## 22. Тестовая стратегия

### Edit Mode

Покрывает:

- прямые и многошаговые планы;
- выбор минимальной стоимости;
- deterministic tie-break;
- Integer/Float/Enum comparisons и effects;
- Add/Subtract;
- cancellation и search limits;
- отсутствие producer;
- dynamic score, cooldown и hysteresis;
- context/distance/action provider cost;
- validation;
- Smart Object queue;
- scheduler;
- graph layout;
- Outpost domain configuration.

### Play Mode

Покрывает:

- полный lifecycle GoapAgent;
- sensor-to-plan-to-executor flow;
- built-in actions;
- action success/failure/cancel;
- interruption policies;
- планирующий budget и обслуживание очереди.

Последний полный локальный прогон: 46 Edit Mode и 8 Play Mode тестов.

## 23. Осознанные ограничения

### Main-thread planning

Текущий Planner синхронный. Scheduler уменьшает spikes, но не выполняет поиск в Job System. Для очень больших Domain или тысяч активных агентов следующий этап - immutable compiled data и worker-thread planning без Unity Object access.

### Локальный выбор target

Action выбирает один подходящий context target перед A*. Planner сравнивает Actions и стоимость этой цели, но не разворачивает каждое дерево как отдельную ветвь состояния. Это удерживает поиск компактным, но для сложной логистики может понадобиться отдельный spatial planner.

### Декларативные Effects

Planner предполагает, что success Action приводит к Effects. Если gameplay-система изменила другой результат, Sensor должен скорректировать World State и вызвать перепланирование.

### Нет полного rollback

Snapshot не является save game. Он не откатывает физическую сцену и внешние компоненты.

### GraphView experimental

Editor representation может потребовать миграции после изменения Unity API. Изоляция dependency ограничивает стоимость такой замены.

### Локальное резервирование

Smart Object registry не решает server authority и persistence. Эти задачи принадлежат сетевому или save слою игры.

## 24. Возможные следующие улучшения

После стабилизации API систему можно развивать в направлениях:

- Package Manager/UXML packaging и semantic versioning;
- migration framework для Domain schema;
- thread-safe compiled data и asynchronous planning;
- hierarchical GOAP для очень длинных планов;
- plan cache по profile + state signature;
- spatial query cache;
- server-authoritative Smart Objects;
- profiler counters и memory allocation benchmark;
- полноценный scene replay поверх snapshots;
- замена GraphView на поддерживаемый Unity graph framework при появлении стабильного API.

Эти улучшения не требуются для текущей демонстрации. Текущая архитектура уже оставляет для них отдельные границы: runtime data, planner, execution adapters, editor representation и diagnostics не смешаны в одном классе.

## 25. Краткое резюме решения

Главные технические свойства системы:

- поведение описывается данными;
- цели оцениваются динамически;
- A* ищет минимальную по стоимости цепочку;
- typed Blackboard выражает ресурсы и потребности без Boolean explosion;
- scene targets и reservations не раздувают planner state;
- универсальный executor уменьшает количество пользовательского кода;
- profiles делают создание ролей повторяемым;
- editor и runtime используют единый источник данных;
- debugger объясняет каждое решение;
- validator ловит ошибки до Play Mode;
- compiled Domain и scheduler позволяют контролировать стоимость массового планирования.

Именно сочетание планировщика, authoring workflow, диагностики и демонстрации превращает реализацию из отдельного учебного алгоритма в цельный Unity-инструмент.
