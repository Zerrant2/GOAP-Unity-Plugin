# GOAP Designer для Unity 6

GOAP Designer - data-driven система искусственного интеллекта для Unity. NPC не выполняют заранее записанное дерево поведения: они оценивают цели, читают текущее состояние мира и через A* находят наиболее выгодную последовательность действий. При изменении ситуации агент может выбрать другую цель, прервать действие по заданной политике и построить новый план.

Проект создавался как практическая работа, но доведён до состояния полноценного инструмента: в нём есть типизированный Blackboard, визуальный редактор Domain, мастер создания контента, профили агентов, библиотека сенсоров и исполнителей, Smart Objects с резервированием, runtime-отладчик, валидация, тесты, benchmark и интерактивная техно-демонстрация.

Текущая версия проекта: Unity `6000.3.19f1` (Unity 6.3 LTS).

## Документация

| Документ | Содержание |
| --- | --- |
| [Демо-сцена GOAP Outpost](Documentation/DEMO_OUTPOST_RU.md) | Запуск, управление, роли NPC, сценарий показа и список демонстрируемых возможностей |
| [Запуск и интеграция](Documentation/INTEGRATION_RU.md) | Установка в другой проект, создание Domain, Actions, Goals, Profile, NPC, Sensors и собственных расширений |
| [Архитектура и технические решения](Documentation/ARCHITECTURE_RU.md) | Полное описание GOAP-модели, A*, runtime-цикла, редактора, Smart Objects, диагностики, производительности и применённых паттернов |
| [Динамическая система принятия решений](My%20project/Assets/GOAP/DECISION_SYSTEM_RU.md) | Краткая памятка по score, cooldown, hysteresis, interruption, контекстным целям и runtime debugger |

## Быстрый запуск

1. Откройте папку `My project` через Unity Hub в Unity `6000.3.19f1`.
2. Дождитесь завершения импорта и компиляции скриптов.
3. Выберите `Tools > GOAP > Tech Demo > Build or Refresh Outpost`.
4. Выберите `Tools > GOAP > Tech Demo > Open Outpost Scene`.
5. Нажмите Play.
6. Во время игры откройте `Tools > GOAP > Runtime Debugger` и выберите агента.
7. Нажмите `Open Graph` в отладчике или откройте `Tools > GOAP > Planner Graph`, чтобы увидеть выбранную цель и найденный план.

Для базовой учебной сцены используйте `Tools > GOAP > Build Demo Project`, затем `Tools > GOAP > Open Demo Scene`.

## Что входит в систему

### GOAP-модель

- `Fact` описывает одно известное агенту значение.
- `Goal` содержит условия активации, желаемое состояние, базовый приоритет, динамические модификаторы и cooldown.
- `Action` задаёт стоимость, предусловия, эффекты, контекстную цель и способ выполнения в сцене.
- `Domain` объединяет Facts, Actions и Goals в одну предметную область.
- `Agent Profile` выбирает доступные конкретному типу NPC Goals и Actions, начальные Facts, Sensors и настройки планировщика.
- `Sensor` переносит состояние Unity-сцены в World State агента.
- `Executor` выполняет найденное Action в игровом мире.
- `Smart Object` представляет резервируемую кровать, еду, дерево, врага или другой объект взаимодействия.

Поток принятия решения:

```text
Unity Scene -> Sensors -> World State -> Goal Selector -> A* Planner -> Plan
      ^                                                          |
      |                    Executors <----------------------------+
      +---------------------- Replanning -------------------------+
```

Последовательность Actions не сохраняется заранее. Планировщик строит её из текущего World State и пересчитывает после существенного изменения мира, завершения или сбоя действия, принудительного запроса и смены цели.

### Типизированный Blackboard

| Тип Fact | Условия | Эффекты |
| --- | --- | --- |
| `Boolean` | `==`, `!=` | `Set` |
| `Integer` | `==`, `!=`, `<`, `<=`, `>`, `>=` | `Set`, `Add`, `Subtract` |
| `Float` | `==`, `!=`, `<`, `<=`, `>`, `>=` | `Set`, `Add`, `Subtract` |
| `Enum` | `==`, `!=` | `Set` |

Пример накопительной цели:

```text
Action: Gather Wood
Precondition: Wood Count < 3
Effect:       Wood Count += 1

Goal: Collect Wood
Desired: Wood Count >= 3
```

### Выбор целей

Итоговая оценка цели складывается из:

```text
Final Score = Base Priority
            + Fact Score Modifiers
            + GoapGoalScorerBehaviour components
```

Модификаторы позволяют плавно повышать срочность голода, усталости, опасности или нехватки ресурсов. `Goal Switch Threshold` защищает от постоянного переключения между почти равными целями, а `Cooldown` временно исключает недавно завершённую цель.

Для действий поддерживаются политики прерывания:

- `Immediate` - остановить текущее действие сразу;
- `FinishCurrentAction` - закончить действие и затем сменить цель;
- `FinishCurrentPlan` - закончить весь текущий план.

### Универсальное выполнение Actions

Большинство поведения настраивается без нового C#-класса. Встроенный исполнитель поддерживает режимы `Wait`, `SmartObjectInteraction` и `Sequence`, а последовательность может содержать шаги:

`FindSmartObject`, `ReserveTarget`, `MoveToTarget`, `Interact`, `Wait`, `ConsumeTarget`, `ReleaseTarget`, `InventoryAdd`, `InventoryRemove`, `SetFact`, `AddFact`, `SubtractFact`, `TriggerAnimation`, `InvokeEvent`.

Действие также может искать Smart Object по категории или Named Target, учитывать расстояние в стоимости плана и получать дополнительную стоимость от компонентов `GoapActionCostProviderBehaviour`.

### Сенсоры

Профильная библиотека содержит сенсоры `SmartObject`, `Inventory`, `Distance`, `Proximity`, `Stat`, `Time`, `ComponentProperty` и `Constant`. Отдельными компонентами доступны manual, trigger и event-сценарии. Режимы обновления: `EveryDecision`, `Interval`, `Manual`, `Event`.

### Визуальный редактор

Откройте `Tools > GOAP > Planner Graph` (`Ctrl+Shift+G`). Редактор предоставляет:

- Fact, Action и Goal ноды с редактируемыми связями;
- создание Fact при протягивании новой связи;
- Undo/Redo, Copy/Paste, Duplicate и multi-selection;
- поиск, вкладки недавних Domain, группы и заметки;
- выравнивание, распределение и сортировку всего графа или выделения;
- компактный и подробный режимы нод;
- фильтрацию Preconditions, Effects и Goal Links;
- Focus-режим для выбранной причинной ветки;
- Minimap и команды `Frame All`/`Frame Plan`;
- ошибки на нодах и быстрые исправления `Create Executor`, `Add Producer`, `Open Sensor`;
- подсветку выбранной Goal, плана, текущего Action, заблокированных Actions и изменённых Facts в Play Mode.

Связи являются представлением сериализованных предусловий и эффектов, поэтому визуальные данные и runtime-модель не расходятся. Зависимость от экспериментального Unity GraphView изолирована в Editor assembly; Runtime от неё не зависит.

### Content Wizard

Откройте `Tools > GOAP > Content Wizard` (`Ctrl+Shift+N`). Вкладки мастера:

- `Agent` - создать NPC или настроить выбранный GameObject;
- `Profile` - собрать профиль из выбранных Goals и автоматически найденных зависимостей;
- `Sensors` - добавить источник значения Fact или Initial Fact;
- `Action` - создать Action по готовому рецепту;
- `Goal` - создать цель и связанные Facts;
- `Presets` - создать связанный набор поведения, профиль, агента и объекты мира;
- `Smart` - создать или настроить Smart Object.

### Runtime Debugger

Откройте `Tools > GOAP > Runtime Debugger` (`Ctrl+Shift+D`) в Play Mode. Доступны вкладки `Overview`, `Facts`, `Goals`, `Actions`, `History`, а также:

- итоговый score каждой цели и вклад каждого модификатора;
- текущий план, его стоимость, время поиска и число раскрытых состояний;
- причины неактивности Goals и блокировки Actions;
- `Pause`, `Step Action`, `Force Replan` и `Abort`;
- автоматические и ручные снимки решений;
- просмотр истории и копирование диагностического отчёта;
- восстановление World State снимка с повторным планированием;
- открытие и подсветка этого же состояния в Planner Graph.

Снимок восстанавливает GOAP World State, но не откатывает Transform, Inventory, резервирования и состояние объектов Unity. Полное воспроизведение сцены требует отдельной игровой save/replay-системы.

### Валидация

Валидатор проверяет отсутствующие ссылки, повторяющиеся ID, некорректные Enum, несовместимые операторы, конфликтующие условия и эффекты, неправильные шаги Sequence, отсутствующие executors, Facts без источника, Actions без эффекта, недостижимые Goals и циклические цепочки без исходного состояния.

### Производительность

- Domain компилируется в индексированное представление, переиспользуемое агентами.
- Boolean Facts упаковываются в bitset; числовые и Enum значения входят в компактный ключ состояния A*.
- `GoapPlannerSettings` ограничивает число состояний, глубину и время одного поиска.
- Поиск поддерживает `CancellationToken`.
- `GoapPlanningScheduler` распределяет массовое перепланирование между кадрами. По умолчанию: не более 16 поисков и 4 мс планирования за кадр.
- Profiler Markers покрывают планирование, компиляцию состояния, поиск, sensors, decision loop, scheduler и benchmark spawn.

`Tools > GOAP > Build Benchmark Scenes` создаёт сцены на 10, 100 и 500 NPC. `Tools > GOAP > Benchmark Dashboard` позволяет переключать `Visual` и `LogicOnly`, задавать бюджет и смотреть среднее время, median, p95, max, FPS, раскрытые состояния и очередь планирования.

## Техно-демо GOAP Outpost

Автоматическая мини-стратегия показывает четырёх специалистов:

| Роль | Основное поведение |
| --- | --- |
| Lumberjack | Резервирует дерево, добывает древесину и относит её на склад |
| Forager | Собирает ягоды и пополняет запас еды |
| Guard | Берёт оружие, атакует монстров и патрулирует периметр |
| Builder | Расходует древесину на ремонт лагеря |

Все жители реагируют на голод, усталость и угрозу. В HUD можно нанимать NPC, менять их роли, регулировать скорость симуляции и вызывать волну монстров. Подробный сценарий презентации находится в [документе демо-сцены](Documentation/DEMO_OUTPOST_RU.md).

## Меню и сочетания клавиш

| Команда | Назначение |
| --- | --- |
| `Tools > GOAP > Content Wizard` | Создание связанного контента, `Ctrl+Shift+N` |
| `Tools > GOAP > Planner Graph` | Визуальный Domain, `Ctrl+Shift+G` |
| `Tools > GOAP > Runtime Debugger` | Отладка NPC, `Ctrl+Shift+D` |
| `Tools > GOAP > Build Demo Project` | Пересоздание базового демо и benchmark |
| `Tools > GOAP > Open Demo Scene` | Открытие базовой сцены |
| `Tools > GOAP > Tech Demo > Build or Refresh Outpost` | Пересоздание техно-демо |
| `Tools > GOAP > Tech Demo > Open Outpost Scene` | Открытие Outpost, `Ctrl+Alt+Shift+G` |
| `Tools > GOAP > Benchmark Dashboard` | Нагрузочные сцены и метрики |
| `Tools > GOAP > Run Automated Tests` | Edit Mode и Play Mode тесты, `Ctrl+Shift+T` |

## Структура репозитория

```text
Practica_unity/
  README.md
  Documentation/
  My project/
    Assets/GOAP/
      Runtime/Core       Domain, Facts, Conditions, World State, validation
      Runtime/Planning   A* planner, goal selector, plans and metrics
      Runtime/Agent      Agent, profiles, sensors, executors and Smart Objects
      Runtime/Demo       Basic demo and benchmark runtime
      Runtime/TechDemo   GOAP Outpost runtime
      Editor             Graph, inspectors, debugger, wizard and builders
      Demo               Generated basic demo and benchmark assets
      TechDemo           Generated Outpost assets and scene
      Tests              Edit Mode and Play Mode tests
```

Папки `Library`, `Temp`, `Logs`, `.vs` и пользовательские файлы IDE не являются частью исходников и исключаются через `.gitignore`.

## Тестирование

Запуск: `Tools > GOAP > Run Automated Tests` или `Ctrl+Shift+T`.

Тесты разделены на Edit Mode и Play Mode. Они покрывают A*, выбор стоимости, типизированные Facts, числовые эффекты, отмену и лимиты поиска, динамический score, hysteresis, cooldown, policies прерывания, планирующий контекст, scheduler, резервирование, валидацию, graph layout, полный runtime-цикл агента и сценарии техно-демо.

Последняя локальная проверка: 46 Edit Mode и 8 Play Mode тестов успешно пройдены.

## Ограничения

- Визуальный слой использует экспериментальный `UnityEditor.Experimental.GraphView`, поэтому он изолирован от Runtime и при изменении Unity может потребовать замены только Editor-представления.
- Эффекты Action описывают ожидаемое логическое состояние. Executor обязан действительно выполнить операцию в сцене и корректно сообщить успех или сбой.
- Система не заменяет NavMesh, анимацию, боевую систему, сохранения или сетевую синхронизацию. Она координирует принятие решений и подключается к этим подсистемам через executors, sensors и events.
- Сгенерированные демо-ассеты предназначены для демонстрации. Для игрового проекта рекомендуется создать собственные Domain и Profile, а demo/runtime держать отдельно от производственного контента.

Полный путь интеграции, включая первый собственный NPC и пользовательские C#-расширения, описан в [инструкции по запуску и интеграции](Documentation/INTEGRATION_RU.md).
