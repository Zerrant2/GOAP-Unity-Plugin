# Запуск и интеграция GOAP Designer

Этот документ описывает полный путь от открытия исходного проекта до добавления GOAP-агента в собственную игру. Для понимания внутренних решений см. [архитектуру](ARCHITECTURE_RU.md), для готовой презентации см. [GOAP Outpost](DEMO_OUTPOST_RU.md).

[Вернуться к основному README](../README.md)

## 1. Требования

- Unity `6000.3.19f1` или совместимая Unity 6.3 LTS;
- Unity Test Framework для Editor assembly и автоматических тестов;
- сохранение assets в текстовом формате: `Edit > Project Settings > Editor > Asset Serialization > Mode: Force Text`;
- `Edit > Project Settings > Editor > Version Control > Mode: Visible Meta Files`;
- для шагов `MoveToTarget` с включённым `Use NavMesh` нужен рабочий NavMesh и корректно размещённый `NavMeshAgent`;
- Animator, Inventory, Stats и игровые компоненты добавляются только для тех Actions и Sensors, которым они нужны.

Core runtime не зависит от URP, Input System или техно-демо. Визуальный редактор работает только в Unity Editor и не попадает в player build.

## 2. Открытие исходного проекта

1. В Unity Hub нажмите `Add > Add project from disk`.
2. Выберите каталог `Practica_unity/My project`.
3. Откройте его в Unity `6000.3.19f1`.
4. Дождитесь окончания импорта и компиляции.
5. Проверьте Console.
6. Запустите `Tools > GOAP > Run Automated Tests`.

Для быстрой функциональной проверки:

1. `Tools > GOAP > Build Demo Project`;
2. `Tools > GOAP > Open Demo Scene`;
3. Play;
4. `Tools > GOAP > Runtime Debugger`.

## 3. Перенос в другой Unity-проект

### Вариант A: перенос всей системы

Скопируйте каталог `Assets/GOAP` вместе со всеми `.meta` файлами. Этот вариант сохраняет runtime, editor tools, tests, базовое демо и Outpost.

После импорта убедитесь, что в Package Manager установлен `Test Framework`, потому что `Practice.GOAP.Editor.asmdef` использует Editor Test Runner для команды автоматического тестирования.

### Вариант B: только runtime и редактор

Для интеграции без демонстрационного контента нужны:

```text
Assets/GOAP/Runtime/
Assets/GOAP/Editor/
```

Каталоги `Demo`, `TechDemo` и `Tests` можно удалить после успешного импорта, если они не нужны. При этом удаляйте соответствующие runtime demo-классы и editor builders согласованно либо сначала оставьте весь пакет и отделите demo после проверки ссылок.

### Рекомендуемая структура собственного контента

Не изменяйте demo Domain как основу production-игры. Создайте отдельный каталог:

```text
Assets/Game/AI/GOAP/
  Domains/
  Profiles/
  Shared/
  Executors/
  Sensors/
  Tests/
```

Сами классы системы останутся в `Assets/GOAP`, а игровые Facts, Actions, Goals, Profiles и расширения будут принадлежать вашему проекту.

## 4. Основные понятия

Перед созданием контента полезно разделить четыре уровня:

| Уровень | Вопрос | Пример |
| --- | --- | --- |
| Fact | Что агент знает сейчас? | `Is Hungry = true`, `Wood Count = 2` |
| Goal | Какого состояния агент хочет достичь? | `Is Hungry = false` |
| Action | Как можно изменить состояние? | `Eat`: Requires Has Food, Effects Is Hungry = false |
| Executor | Что реально происходит в Unity? | Подойти к столу, проиграть анимацию, удалить еду |

Sensors записывают наблюдения в World State. Planner использует только Facts, Preconditions, Effects и Cost. Executor работает со сценой после того, как план найден.

## 5. Первый Domain

1. Откройте `Tools > GOAP > Planner Graph` (`Ctrl+Shift+G`).
2. Нажмите `New Domain`.
3. Сохраните asset в `Assets/Game/AI/GOAP/Domains`.
4. Создайте Facts, Actions и Goals через кнопки `+` в Library или через правый клик по полотну.
5. Настройте выбранную ноду в Inspector справа.
6. Нажмите `Validate` и затем `Save`.

Domain является общей библиотекой предметной области. Профиль конкретного NPC может использовать весь Domain или только выбранное подмножество Actions и Goals.

## 6. Создание Facts

Fact должен описывать минимальное логическое знание, полезное для выбора целей или построения плана.

### Поддерживаемые типы

| Тип | Использование |
| --- | --- |
| Boolean | наличие объекта, флаг опасности, простое состояние |
| Integer | количество предметов, патроны, уровень ресурса |
| Float | здоровье, голод, дистанция, время |
| Enum | режим, настроение, тип приказа, стадия процесса |

### Рекомендации по именованию

- используйте состояние, а не команду: `Has Food`, а не `Find Food`;
- отделяйте локальные и общие значения: `Carry Wood` и `Wood Stockpile`;
- не храните Transform или ссылку на текущую цель в Fact;
- не создавайте несколько Boolean Facts там, где взаимоисключающий Enum выражает состояние точнее;
- задавайте реалистичное default value: оно участвует в новом World State до первого обновления сенсора.

### Условия и эффекты

Boolean и Enum поддерживают `==` и `!=`. Integer и Float также поддерживают `<`, `<=`, `>`, `>=`.

Эффекты:

- `Set` заменяет значение;
- `Add` прибавляет Integer или Float;
- `Subtract` вычитает Integer или Float.

Не используйте `Add` и `Subtract` для Boolean или Enum. Валидатор отметит такую конфигурацию.

## 7. Создание Action

### Через Content Wizard

1. Откройте `Tools > GOAP > Content Wizard` (`Ctrl+Shift+N`).
2. Перейдите на вкладку `Action`.
3. Выберите Domain.
4. Введите имя, описание и Cost.
5. Выберите рецепт.
6. Добавьте Preconditions и Effects.
7. При необходимости создайте новый Fact прямо в этом окне.
8. Нажмите кнопку создания Action.

Готовые рецепты:

- `Wait`;
- `MoveToNamedTarget`;
- `SmartObjectInteraction`;
- `GatherResource`;
- `ConsumeInventory`;
- `TriggerAnimation`;
- `InvokeEvent`.

### Через Planner Graph

1. Создайте Action-ноду.
2. Протяните `Conditions` от Fact к `Preconditions` Action.
3. Протяните `Effects` от Action к `Effects` Fact.
4. Выберите Action и уточните comparison, value и effect operation в Inspector.
5. Настройте Cost, Targeting, Interruption и Execution.

Удаление Edge удаляет соответствующее условие из Action. Создание и удаление поддерживают Undo/Redo.

### Стоимость

`Cost` должна выражать предпочтительность, а не длительность в секундах. Более дешёвая достижимая цепочка выигрывает. Для пространственного выбора задайте:

- `Target Mode: SmartObjectCategory` или `NamedTarget`;
- идентификатор категории/цели;
- `Distance Cost Per Unit`.

Итоговая стоимость может включать base cost, расстояние и пользовательские `GoapActionCostProviderBehaviour`.

### Политика прерывания

- `Immediate` подходит для фонового, короткого и безопасно отменяемого действия;
- `FinishCurrentAction` подходит для анимации, транзакции, добычи и доставки;
- `FinishCurrentPlan` используйте для последовательности, которую нельзя логически разрывать.

Любой Executor обязан корректно обработать Cancel: остановить корутину, освободить резервирование и не оставить частично применённые данные.

## 8. Встроенный Executor

`GoapBuiltInActionBehaviour` автоматически добавляется вместе с `GoapAgentAuthoring`. Он поддерживает три готовых режима и пользовательский режим.

### Wait

Ожидает указанную Duration и завершает Action.

### SmartObjectInteraction

Готовый сценарий может найти объект категории, зарезервировать его, подойти, взаимодействовать, изменить Inventory, проиграть Animator Trigger и при необходимости потребить объект.

### Sequence

Последовательность собирается из шагов:

| Step | Назначение |
| --- | --- |
| `FindSmartObject` | Найти ближайший доступный объект по Category |
| `ReserveTarget` | Получить место или встать в очередь с timeout |
| `MoveToTarget` | Подойти напрямую или через NavMesh |
| `Interact` | Вызвать взаимодействие Smart Object |
| `Wait` | Подождать Duration |
| `ConsumeTarget` | Сделать расходуемый объект недоступным |
| `ReleaseTarget` | Освободить объект |
| `InventoryAdd` | Добавить item в `GoapInventory` |
| `InventoryRemove` | Удалить item из `GoapInventory` |
| `SetFact` | Установить Fact |
| `AddFact` | Увеличить числовой Fact |
| `SubtractFact` | Уменьшить числовой Fact |
| `TriggerAnimation` | Вызвать trigger в Animator |
| `InvokeEvent` | Вызвать событие по ID в `GoapActionEventReceiver` |

Порядок шагов важен. Типовая добыча:

```text
FindSmartObject(Tree)
ReserveTarget
MoveToTarget
Interact
Wait
InventoryAdd(Wood, 1)
ReleaseTarget
```

Валидатор проверит отсутствующую цель, некорректную последовательность, недостающий Inventory, Animator, event ID и неправильный тип Fact.

## 9. Создание Goal

1. В Content Wizard откройте вкладку `Goal` или создайте Goal в графе.
2. Добавьте `Activation Conditions`: когда цель имеет смысл рассматривать.
3. Добавьте `Desired State`: что должно стать истинным после плана.
4. Задайте `Base Priority`.
5. При необходимости настройте `Cooldown Seconds` и `Fact Score Modifiers`.

Пример:

```text
Goal: Satisfy Hunger
Activation: Hunger >= 65 AND Food Stockpile >= 1
Desired: Hunger <= 20
Base Priority: 85
```

Activation Conditions не должны описывать уже достигнутый результат. Если Desired State уже выполнен, цель считается завершённой и не выбирается.

### Динамический score

Fact Score Modifier отображает входной диапазон Fact в диапазон добавочного score. Например:

```text
Hunger 65..100 -> +0..35
```

Чем сильнее голод, тем выше срочность. Для сложной сценовой логики добавьте наследника `GoapGoalScorerBehaviour` на GameObject агента.

`Goal Switch Threshold` находится в Agent Profile и задаёт минимальное преимущество новой цели над текущей. Это защищает от постоянного переключения при близких оценках.

## 10. Создание Agent Profile

Профиль является основной точкой переиспользования ролей.

### Автоматическая композиция

1. Откройте `Tools > GOAP > Content Wizard`.
2. Выберите `Profile`.
3. Укажите Domain и имя профиля.
4. Выберите нужные Goals.
5. Проверьте блок `Generated Profile`.
6. Включите `Include Alternatives`, если нужны все Actions, способные производить состояние, а не только самый дешёвый вариант.
7. При необходимости включите создание Scene Agent.
8. Нажмите `Create Composed Profile`.

Composer идёт от Desired State целей назад, подбирает Actions-производители и рекурсивно добавляет их Preconditions. Activation Conditions становятся initial facts или требуют Sensors. Если цепочка не имеет исходного состояния, мастер покажет `No grounded Action chain can achieve`.

### Содержимое профиля

- `Domain`;
- разрешённые `Actions`;
- разрешённые `Goals`;
- `Initial Facts`;
- `Sensors`;
- `Decision Interval`;
- `Goal Switch Threshold`;
- `Max Expanded States`;
- `Max Plan Depth`;
- `Max Planning Milliseconds`;
- `Log Decisions`.

Пустой список Actions или Goals означает использование полного списка из Domain.

## 11. Библиотека сенсоров

Во вкладке `Sensors` Content Wizard можно назначить Fact источник данных или initial value.

| Sensor kind | Источник |
| --- | --- |
| `SmartObject` | наличие доступного Smart Object заданной категории |
| `Inventory` | количество item в `GoapInventory` |
| `Distance` | расстояние до Named Target |
| `Proximity` | объекты в радиусе с фильтром Layer и Tag |
| `Stat` | значение из `GoapStatSource` |
| `Time` | игровое время с scale и offset |
| `ComponentProperty` | поле или property компонента по имени типа и member |
| `Constant` | постоянное типизированное значение |

Режимы обновления:

- `EveryDecision`: перед каждым decision tick;
- `Interval`: не чаще заданного интервала;
- `Manual`: только после явного запроса;
- `Event`: по событию и `RequestRefresh()`.

Также доступны готовые компоненты `GoapDistanceSensor`, `GoapInventorySensor`, `GoapSmartObjectSensor`, `GoapTriggerSensor`, `GoapBooleanEventSensor` и `GoapManualSensor`.

Не используйте reflection-сенсор `ComponentProperty` в очень горячем цикле сотен NPC без измерения. Для часто обновляемой игровой статистики предпочтителен типизированный Sensor.

## 12. Добавление NPC в сцену

### Через Content Wizard

1. Выберите готовый GameObject NPC в Hierarchy.
2. Откройте `Content Wizard > Agent`.
3. Перетащите объект в `Existing Object`.
4. Назначьте `Agent Profile`.
5. При необходимости включите `Inventory` и `Stats`.
6. Нажмите `Setup Selected Object`.

Если `Existing Object` оставить пустым, мастер создаст новый объект. `Visible Placeholder` создаёт видимую Capsule для проверки.

### Вручную

1. Добавьте `GoapAgentAuthoring`.
2. Назначьте Profile.
3. Добавьте значения `Initial Fact Overrides`, если конкретный экземпляр отличается от профиля.
4. Добавьте `Named Targets` в виде пары ID + Transform.
5. Оставьте `Apply On Awake` включённым или нажмите `Apply Profile` в Inspector.

`RequireComponent` автоматически добавит:

- `GoapAgent`;
- `GoapBuiltInActionBehaviour`;
- `GoapProfileSensorBehaviour`.

Отдельный bootstrap-скрипт для каждого типа NPC не требуется.

## 13. Smart Objects

### Создание

1. Откройте `Content Wizard > Smart`.
2. Выберите существующий GameObject или оставьте поле пустым для placeholder.
3. Задайте Category.
4. Задайте Capacity.
5. Укажите, расходуется ли объект после использования.
6. Создайте объект.

Action и Sensor должны использовать одинаковую строку Category. Для Named Target ID также важно точное совпадение.

### Резервирование

Smart Object хранит владельцев, очередь и timeout. `RequestReservation` либо выдаёт место, либо ставит агента в FIFO-очередь. Просроченные запросы и уничтоженные агенты удаляются, свободное место передаётся следующему.

Рекомендуемый порядок Sequence:

```text
Find -> Reserve -> Move -> Interact -> Release
```

При отмене Action встроенный Executor автоматически освобождает цель. В собственном Executor выполняйте такую же очистку в `OnCancelled`.

## 14. Быстрые presets

Вкладка `Content Wizard > Presets` создаёт связанный пакет поведения.

- `BasicNeeds`: Hunger/Fatigue Facts, еда, сон, Goals, Sensors, профиль и объекты мира.
- `ResourceGathering`: availability и inventory Facts, резервируемая добыча, Goal накопления, Sensors, профиль, агент и ресурсный Smart Object.

Это самый быстрый способ получить рабочего NPC и затем разобрать созданные assets в Planner Graph.

## 15. Пользовательский Executor

Создавайте собственный Executor, когда встроенных шагов недостаточно для боевой системы, сложной анимации, диалога или асинхронного gameplay API.

```csharp
using System.Collections;
using Practice.GOAP;
using UnityEngine;

public sealed class CraftActionBehaviour : GoapActionBehaviour
{
    [SerializeField] private CraftingStation _station;

    public override GoapExecutorDiagnostic EvaluateStart(GoapActionContext context)
    {
        return _station != null && _station.CanCraft
            ? GoapExecutorDiagnostic.Ready()
            : GoapExecutorDiagnostic.Blocked(
                GoapExecutorIssueCode.RequiredComponentMissing,
                "Crafting station is unavailable");
    }

    protected override IEnumerator Perform(GoapActionContext context)
    {
        yield return _station.Craft();

        if (_station.LastCraftSucceeded)
        {
            Succeed();
        }
        else
        {
            Fail("Crafting failed");
        }
    }

    protected override void OnCancelled(GoapActionContext context)
    {
        _station?.CancelCraft();
    }
}
```

В Inspector компонента задайте `Executor Id`, совпадающий с `Executor Id` Action. `EvaluateStart` не должен изменять сцену: это диагностическая проверка доступности. `Perform` обязан вызвать `Succeed()` или `Fail(reason)`.

После успеха агент применяет логические Effects Action. Если Executor сам вычисляет значение, используйте `context.StageFact(...)` и пометьте обработанный эффект, чтобы не применить его дважды.

## 16. Пользовательский Sensor

```csharp
using Practice.GOAP;
using UnityEngine;

public sealed class HealthSensor : GoapSensorBehaviour
{
    [SerializeField] private GoapFact _healthFact;
    [SerializeField] private HealthComponent _health;

    public override void Sense(GoapAgent agent, GoapWorldState state)
    {
        if (_healthFact != null && _health != null)
        {
            state.Set(_healthFact, _health.CurrentHealth);
        }
    }
}
```

Добавьте компонент на тот же GameObject, что и `GoapAgent`. Для event-driven источника вызовите `RequestRefresh()` при изменении данных и затем `agent.ForceReplan()` только если решение действительно нужно пересчитать немедленно.

## 17. Пользовательский score и стоимость

### Goal scorer

```csharp
public sealed class OrderGoalScorer : GoapGoalScorerBehaviour
{
    public override float EvaluateScore(
        GoapAgent agent,
        GoapGoalDefinition goal,
        GoapWorldState worldState)
    {
        return HasUrgentPlayerOrder() ? 100f : 0f;
    }
}
```

### Action cost provider

```csharp
public sealed class DangerCostProvider : GoapActionCostProviderBehaviour
{
    public override float EvaluateAdditionalCost(
        GoapAgent agent,
        GoapActionDefinition action,
        Transform target)
    {
        return target != null && IsAreaDangerous(target.position) ? 25f : 0f;
    }
}
```

Дополнительная стоимость должна быть неотрицательной и детерминированной в пределах одного планирования. Не выполняйте здесь дорогой поиск пути для каждого раскрываемого состояния; кэшируйте пространственные оценки до запуска A*.

## 18. Работа с Planner Graph

### Навигация

- колесо мыши меняет масштаб;
- средняя кнопка перемещает область просмотра;
- рамка выделяет несколько нод;
- поиск фильтрует Library и затемняет неподходящие ноды;
- вкладки под toolbar переключают недавние Domain;
- Minimap помогает в большом графе.

### Читаемость

- `Sort Graph` раскладывает Domain по причинным слоям;
- правый клик `Layout > Sort Selection` меняет только выбранную область;
- `Align Left`, `Align Top`, `Distribute Horizontally/Vertically` правят ручную раскладку;
- `Focus` оставляет контрастной только связанную ветку выбранной ноды;
- `Details` переключает компактные и подробные ноды;
- `Connections` отдельно скрывает Preconditions, Effects и Goal Links;
- `Frame All` показывает весь Domain;
- `Frame Plan` показывает текущую runtime-ветку.

### Организация

- `Ctrl+C`/`Ctrl+V` дублируют выбранные определения;
- контекстное меню содержит `Duplicate Selection`;
- `Annotations > Group Selection` создаёт сохраняемую группу;
- `Annotations > Note` создаёт заметку;
- все изменения раскладки поддерживают Undo/Redo.

## 19. Валидация и исправление ошибок

Нажмите `Validate` в Planner Graph после изменения Domain. Цвет и badge на ноде показывают локальную проблему, а панель справа содержит полное сообщение.

Быстрые исправления:

- `Create Executor`: создать заготовку недостающего компонента;
- `Add Producer`: добавить Action, создающий требуемый Fact;
- `Open Sensor`: открыть мастер источника внешнего Fact.

Не подавляйте warning автоматически. Fact без производителя может быть корректным внешним входом, но тогда его должен задавать Sensor или Initial Fact.

## 20. Runtime-отладка

1. Войдите в Play Mode.
2. Откройте `Tools > GOAP > Runtime Debugger` (`Ctrl+Shift+D`).
3. Включите `Follow Selection` и выберите NPC или нажмите `Find`.
4. Проверьте вкладки:
   - `Overview`: статус, план, стоимость и главные блокировки;
   - `Facts`: текущий World State;
   - `Goals`: score, cooldown, activation и desired state;
   - `Actions`: executor, context cost, preconditions и причины блокировки;
   - `History`: снимки решений и события.
5. Используйте `Pause`, `Step Action`, `Force Replan`, `Abort`, `Capture` и `Copy`.
6. Нажмите `Open Graph` и `Frame Plan` для визуальной трассировки.

При проблеме сначала смотрите не Console, а причины в Goals и Actions: отладчик различает неподходящее предусловие, отсутствующий Executor, занятый Smart Object, недостаток Inventory, invalid NavMesh path и недоступный planning context.

## 21. Производственные настройки

### Planner limits

Начальные значения профиля:

```text
Max Expanded States:       5000
Max Plan Depth:            32
Max Planning Milliseconds: 10
```

Уменьшайте лимиты только после измерения. Слишком маленькое значение превращает достижимую цель в `StateLimitReached`, `DepthLimitReached` или `TimeLimitReached`.

### Scheduler

Глобальный `GoapPlanningScheduler` по умолчанию допускает 16 поисков и 4 мс суммарного планирования за кадр. Остальные запросы обслуживаются по очереди. Для проекта с массовым спавном настройте budget по результатам benchmark, а не по среднему FPS одной сцены.

### Decision Interval и Sensors

- не запускайте decision loop каждый Update без необходимости;
- используйте Interval для медленных потребностей и времени;
- используйте Event для редких критических изменений;
- разделяйте изменение World State и принудительное перепланирование;
- не вызывайте тяжёлую Physics-проверку несколькими сенсорами, если результат можно разделить.

## 22. Тестирование интеграции

Минимальный набор проверок после переноса:

1. Domain проходит `Validate` без ошибок.
2. Для каждой активационной ветки существует достижимый Desired State.
3. Каждый Action имеет встроенный режим или подходящий Executor.
4. Все внешние Facts имеют Sensor или Initial Fact.
5. При Cancel освобождаются Smart Objects и gameplay locks.
6. NPC корректно восстанавливается после недоступной цели.
7. Runtime Debugger объясняет ожидаемую причину решения.
8. Edit Mode и Play Mode tests проходят.
9. Сцена с максимальным ожидаемым числом NPC проверена через Profiler.

Команда полного набора: `Tools > GOAP > Run Automated Tests` (`Ctrl+Shift+T`).

## 23. Частые проблемы

### NPC ничего не делает

- Profile не назначен или не применён;
- у Goal не выполнены Activation Conditions;
- Desired State уже выполнен;
- нет цепочки Actions до цели;
- отсутствует Sensor для внешнего Fact;
- нет Executor;
- Action context не может найти Smart Object или Named Target;
- scheduler отложил планирование до следующего кадра.

Откройте Runtime Debugger, затем Goals и Actions.

### Action появился в Domain, но агент его не использует

- Profile содержит явный список Actions и новый asset в него не добавлен;
- Action не производит Fact, нужный активной Goal;
- есть более дешёвая цепочка;
- Preconditions недостижимы;
- стоимость контекста бесконечна из-за отсутствующей цели.

### Два NPC идут к одному ресурсу

Проверьте, что Action содержит `ReserveTarget`, Smart Object имеет правильную Capacity, а Category совпадает. Собственный Executor должен освобождать цель при успехе, сбое и Cancel.

### Граф показывает warning для внешнего Fact

Добавьте Sensor через `Open Sensor` или явно задайте Initial Fact в Profile. Warning исчезнет после появления известного источника.

### После Restore & Replan сцена не вернулась назад

Команда восстанавливает только GOAP World State. Она не откатывает Transform, Inventory, Smart Objects и игровые компоненты.

## Контрольный пример интеграции

Для NPC, который должен добыть еду и затем поспать:

1. Facts: `Hunger: Float`, `Energy: Float`, `Carry Food: Integer`, `Food Available: Boolean`, `Bed Available: Boolean`.
2. Actions: `Gather Food`, `Eat`, `Sleep`.
3. Goals: `Satisfy Hunger`, `Recover Energy`.
4. Smart Objects: категории Food и Bed.
5. Sensors: Inventory/Stat/SmartObject.
6. Profile: обе Goals и три Actions.
7. Agent: `GoapAgentAuthoring` + Profile.
8. Play Mode: Runtime Debugger должен показать `Gather Food -> Eat`, а позднее `Sleep`.

После этого добавление новой роли обычно сводится к новому Profile, а не к новому Agent-классу.
