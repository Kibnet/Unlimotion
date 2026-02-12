module unlimotion_file_mode {

/// Quint-спецификация файлового режима Unlimotion.
/// Модель отражает локальную логику TaskTreeManager для FileStorage.
/// Scope:
/// - покрывает операции Add/AddChild/Delete/Update(без/с completion), Block/Unblock,
///   AddNewParent/Move/DeleteParentChild, CloneTask, HandleTaskCompletionChange;
/// - не моделирует retry/таймауты/ошибки I/O;
/// - не моделирует фактические DateTime-значения (только флаги наличия дат).

// === Доменные типы ===
type TaskState = Active | Completed | Archived

type Task = {
  state: TaskState,
  canBeCompleted: bool,
  unlocked: bool,
  hasCompletedTime: bool,
  hasArchiveTime: bool,
  hasPlannedBegin: bool,
  hasPlannedEnd: bool,
  repeaterEnabled: bool,
  wanted: bool,
  version: int,
  children: Set[str],
  parents: Set[str],
  blocks: Set[str],
  blockedBy: Set[str]
}

pure val mkSavedTask: Task = {
  state: Active,
  canBeCompleted: true,
  unlocked: false,
  hasCompletedTime: false,
  hasArchiveTime: false,
  hasPlannedBegin: false,
  hasPlannedEnd: false,
  repeaterEnabled: false,
  wanted: false,
  version: 1,
  children: Set(),
  parents: Set(),
  blocks: Set(),
  blockedBy: Set()
}

pure def cloneForRepeater(t: Task): Task = {
  // Copy-модель для clone в completion-path; обратные ссылки достраиваются
  // отдельными шагами в setCompletion.
  state: Active,
  canBeCompleted: true,
  unlocked: false,
  hasCompletedTime: false,
  hasArchiveTime: false,
  hasPlannedBegin: t.hasPlannedBegin,
  hasPlannedEnd: t.hasPlannedEnd,
  repeaterEnabled: t.repeaterEnabled,
  wanted: t.wanted,
  version: 1,
  children: t.children,
  parents: Set(),
  blocks: t.blocks,
  blockedBy: t.blockedBy
}

pure def cloneForCloneTask(t: Task): Task = {
  // CloneTask ctor копирует метаданные, но связи строит отдельными шагами.
  state: Active,
  canBeCompleted: true,
  unlocked: false,
  hasCompletedTime: false,
  hasArchiveTime: false,
  hasPlannedBegin: t.hasPlannedBegin,
  hasPlannedEnd: t.hasPlannedEnd,
  repeaterEnabled: t.repeaterEnabled,
  wanted: t.wanted,
  version: 1,
  children: Set(),
  parents: Set(),
  blocks: Set(),
  blockedBy: Set()
}

pure val Ids: Set[str] = Set("a", "b", "c", "d", "e", "f")

var tasks: str -> Task

// === Базовые предикаты ===
def hasTask(id) = tasks.keys().contains(id)

pure def hasTaskIn(m, id) = m.keys().contains(id)

pure def isDoneIn(m, id) =
  m.get(id).state == Completed or m.get(id).state == Archived

// В коде отсутствующие зависимости не блокируют доступность (Load == null игнорируется).
pure def expectedCanBeCompletedIn(m, id) =
  m.get(id).children.forall(c => not(hasTaskIn(m, c)) or isDoneIn(m, c)) and
  m.get(id).blockedBy.forall(b => not(hasTaskIn(m, b)) or isDoneIn(m, b))

def expectedCanBeCompleted(id) = expectedCanBeCompletedIn(tasks, id)

pure def applyAvailability(t, allowed) =
  if (allowed) {
    t.with("canBeCompleted", true).with("unlocked", true)
  } else {
    t.with("canBeCompleted", false).with("unlocked", false)
  }

pure def addParentChildIn(m, parent, child) =
  if (parent == child) {
    m
  } else {
    m
      .set(parent, m.get(parent).with("children", m.get(parent).children.union(Set(child))))
      .set(child, m.get(child).with("parents", m.get(child).parents.union(Set(parent))))
  }

pure def removeParentChildIn(m, parent, child) =
  m
    .set(parent, m.get(parent).with("children", m.get(parent).children.exclude(Set(child))))
    .set(child, m.get(child).with("parents", m.get(child).parents.exclude(Set(parent))))

// blocking blocks blocked
pure def addBlockingIn(m, blocking, blocked) =
  if (blocking == blocked) {
    m
  } else {
    m
      .set(blocking, m.get(blocking).with("blocks", m.get(blocking).blocks.union(Set(blocked))))
      .set(blocked, m.get(blocked).with("blockedBy", m.get(blocked).blockedBy.union(Set(blocking))))
  }

pure def removeBlockingIn(m, blocking, blocked) =
  m
    .set(blocking, m.get(blocking).with("blocks", m.get(blocking).blocks.exclude(Set(blocked))))
    .set(blocked, m.get(blocked).with("blockedBy", m.get(blocked).blockedBy.exclude(Set(blocking))))

// === Транзитивный пересчет доступности (как в текущем коде) ===
pure def affectedNeighborsIn(m, id) =
  if (hasTaskIn(m, id)) {
    m.get(id).parents.union(m.get(id).blocks).intersect(m.keys())
  } else {
    Set()
  }

pure def expandAffectedIn(m, ids) =
  ids.union(ids.map(i => affectedNeighborsIn(m, i)).flatten())

pure def affectedClosureIn(m, seeds) =
  m.keys().fold(seeds.intersect(m.keys()), (acc, _) => expandAffectedIn(m, acc))

pure def recalcFrom(m, seeds) = {
  val scope = affectedClosureIn(m, seeds)
  scope.fold(m, (acc, i) =>
    acc.set(i, applyAvailability(acc.get(i), expectedCanBeCompletedIn(acc, i))))
}

// === Операции TaskTreeManager ===
action init = tasks' = Map()

action recalcAvailability(id) = all {
  hasTask(id),
  tasks' = recalcFrom(tasks, Set(id))
}

// AddTask(change, currentTask = null)
action addTask(id) = all {
  not(hasTask(id)),
  tasks' = tasks.put(id, mkSavedTask)
}

// AddTask(change, currentTask != null, isBlocked = false)
action addTaskSibling(newId, current) = {
  val ok = hasTask(current) and not(hasTask(newId))
  val parentSet = if (hasTask(current)) { tasks.get(current).parents.intersect(tasks.keys()) } else { Set() }
  val base = if (ok) { tasks.put(newId, mkSavedTask) } else { tasks }
  val t1 = parentSet.fold(base, (acc, p) => addParentChildIn(acc, p, newId))
  all {
    ok,
    tasks' = recalcFrom(t1, parentSet)
  }
}

// AddTask(change, currentTask != null, isBlocked = true)
action addBlockedSibling(newId, current) = {
  val ok = hasTask(current) and not(hasTask(newId))
  val parentSet = if (hasTask(current)) { tasks.get(current).parents.intersect(tasks.keys()) } else { Set() }
  val base = if (ok) { tasks.put(newId, mkSavedTask) } else { tasks }
  val t1 = parentSet.fold(base, (acc, p) => addParentChildIn(acc, p, newId))
  val t2 = if (ok) { addBlockingIn(t1, current, newId) } else { t1 }
  all {
    ok,
    tasks' = recalcFrom(t2, parentSet.union(Set(newId)))
  }
}

// AddChildTask(change, currentTask)
action addChildTask(parent, childNewId) = {
  val ok = hasTask(parent) and not(hasTask(childNewId))
  val base = if (ok) { tasks.put(childNewId, mkSavedTask) } else { tasks }
  val t1 = if (ok) { addParentChildIn(base, parent, childNewId) } else { base }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(parent))
  }
}

// Внутренняя relation-операция (без создания новой задачи)
action addChild(parent, child) = {
  val ok = hasTask(parent) and hasTask(child)
  val t1 = if (ok) { addParentChildIn(tasks, parent, child) } else { tasks }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(parent))
  }
}

action deleteParentChild(parent, child) = {
  val ok = hasTask(parent) and hasTask(child)
  val t1 = if (ok) { removeParentChildIn(tasks, parent, child) } else { tasks }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(parent))
  }
}

// AddNewParentToTask(change, additionalParent)
action addNewParent(child, parent) = {
  val ok = hasTask(child) and hasTask(parent)
  val t1 = if (ok) { addParentChildIn(tasks, parent, child) } else { tasks }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(child).union(Set(parent)))
  }
}

// MoveTaskToNewParent(change, newParent, prevParent != null)
action moveTaskToNewParent(child, newParent, oldParent) = {
  val ok = hasTask(child) and hasTask(newParent) and hasTask(oldParent)
  val t1 =
    if (ok) {
      addParentChildIn(tasks, newParent, child)
    } else {
      tasks
    }
  val t2 =
    if (ok) {
      removeParentChildIn(t1, oldParent, child)
    } else {
      t1
    }
  all {
    ok,
    tasks' = recalcFrom(t2, Set(child).union(Set(newParent)).union(Set(oldParent)))
  }
}

action blockTask(blocking, blocked) = {
  val ok = hasTask(blocking) and hasTask(blocked)
  val t1 = if (ok) { addBlockingIn(tasks, blocking, blocked) } else { tasks }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(blocked))
  }
}

action unblockTask(blocking, blocked) = {
  val ok = hasTask(blocking) and hasTask(blocked)
  val t1 = if (ok) { removeBlockingIn(tasks, blocking, blocked) } else { tasks }
  all {
    ok,
    tasks' = recalcFrom(t1, Set(blocked))
  }
}

// CloneTask(change, stepParents = [])
action cloneTask(source, cloneId) = {
  val ok = hasTask(source) and not(hasTask(cloneId))
  val src = if (hasTask(source)) { tasks.get(source) } else { mkSavedTask }
  val base = if (ok) { tasks.put(cloneId, cloneForCloneTask(src)) } else { tasks }

  val existingChildren = if (ok) { src.children.intersect(base.keys()) } else { Set() }
  val t1 = existingChildren.fold(base, (acc, c) => addParentChildIn(acc, cloneId, c))

  val existingBlockers = if (ok) { src.blockedBy.intersect(t1.keys()) } else { Set() }
  val t2 = existingBlockers.fold(t1, (acc, b) => addBlockingIn(acc, b, cloneId))

  val existingBlocks = if (ok) { src.blocks.intersect(t2.keys()) } else { Set() }
  val t3 = existingBlocks.fold(t2, (acc, d) => addBlockingIn(acc, cloneId, d))

  all {
    ok,
    tasks' = recalcFrom(t3, Set(cloneId).union(existingBlocks))
  }
}

// CloneTask(change, stepParents = [parent])
action cloneTaskIntoParent(source, parent, cloneId) = {
  val ok = hasTask(source) and hasTask(parent) and not(hasTask(cloneId))
  val src = if (hasTask(source)) { tasks.get(source) } else { mkSavedTask }
  val base = if (ok) { tasks.put(cloneId, cloneForCloneTask(src)) } else { tasks }

  val existingChildren = if (ok) { src.children.intersect(base.keys()) } else { Set() }
  val t1 = existingChildren.fold(base, (acc, c) => addParentChildIn(acc, cloneId, c))

  val existingBlockers = if (ok) { src.blockedBy.intersect(t1.keys()) } else { Set() }
  val t2 = existingBlockers.fold(t1, (acc, b) => addBlockingIn(acc, b, cloneId))

  val existingBlocks = if (ok) { src.blocks.intersect(t2.keys()) } else { Set() }
  val t3 = existingBlocks.fold(t2, (acc, d) => addBlockingIn(acc, cloneId, d))

  val t4 = if (ok) { addParentChildIn(t3, parent, cloneId) } else { t3 }

  all {
    ok,
    tasks' = recalcFrom(t4, Set(cloneId).union(existingBlocks).union(Set(parent)))
  }
}

// UpdateTask(change) when IsCompleted did not change
action updateMetadata(id, newWanted, newRepeaterEnabled, newHasPlannedBegin, newHasPlannedEnd) = {
  val t1 =
    if (hasTask(id)) {
      tasks.set(id,
        tasks.get(id)
          .with("wanted", newWanted)
          .with("repeaterEnabled", newRepeaterEnabled)
          .with("hasPlannedBegin", newHasPlannedBegin)
          .with("hasPlannedEnd", newHasPlannedEnd))
    } else {
      tasks
    }
  all {
    hasTask(id),
    tasks' = t1
  }
}

// HandleTaskCompletionChange(change)
action setCompletion(id, newState, cloneId) = {
  val old = if (hasTask(id)) { tasks.get(id) } else { mkSavedTask }

  val needsRepeaterClone =
    (newState == Completed) and
    (not(old.hasCompletedTime)) and
    old.repeaterEnabled and
    old.hasPlannedBegin

  val canCreateClone = (not(hasTask(cloneId)))

  val withDates =
    if (newState == Completed) {
      old.with("state", Completed).with("hasCompletedTime", true).with("hasArchiveTime", false)
    } else if (newState == Active) {
      old.with("state", Active).with("hasCompletedTime", false).with("hasArchiveTime", false)
    } else {
      // В коде archived path не очищает CompletedDateTime принудительно.
      old.with("state", Archived).with("hasArchiveTime", true)
    }

  val base = if (hasTask(id)) { tasks.set(id, withDates) } else { tasks }

  val withClone =
    if (needsRepeaterClone and canCreateClone) {
      base.put(cloneId, cloneForRepeater(old))
    } else {
      base
    }

  val withCloneChildren =
    if (needsRepeaterClone and canCreateClone) {
      old.children
        .intersect(withClone.keys())
        .fold(withClone, (acc, c) => addParentChildIn(acc, cloneId, c))
    } else {
      withClone
    }

  val withCloneBlockers =
    if (needsRepeaterClone and canCreateClone) {
      old.blockedBy
        .intersect(withCloneChildren.keys())
        .fold(withCloneChildren, (acc, b) => addBlockingIn(acc, b, cloneId))
    } else {
      withCloneChildren
    }

  val withCloneBlocks =
    if (needsRepeaterClone and canCreateClone) {
      old.blocks
        .intersect(withCloneBlockers.keys())
        .fold(withCloneBlockers, (acc, d) => addBlockingIn(acc, cloneId, d))
    } else {
      withCloneBlockers
    }

  val seeds =
    if (needsRepeaterClone and canCreateClone) {
      Set(id).union(Set(cloneId))
    } else {
      Set(id)
    }

  all {
    hasTask(id),
    (newState == Active or newState == Completed or newState == Archived),
    (not(needsRepeaterClone)) or canCreateClone,
    tasks' = recalcFrom(withCloneBlocks, seeds)
  }
}

// DeleteTask(change)
action deleteTask(id) = {
  val old = if (hasTask(id)) { tasks.get(id) } else { mkSavedTask }
  val seeds = if (hasTask(id)) { old.parents.union(old.blocks) } else { Set() }
  val remaining = tasks.keys().exclude(Set(id))

  val t1 =
    remaining.fold(Map(), (acc, k) =>
      acc.put(
        k,
        tasks.get(k)
          .with("children", tasks.get(k).children.exclude(Set(id)))
          .with("parents", tasks.get(k).parents.exclude(Set(id)))
          .with("blocks", tasks.get(k).blocks.exclude(Set(id)))
          .with("blockedBy", tasks.get(k).blockedBy.exclude(Set(id)))
      ))

  all {
    hasTask(id),
    tasks' = recalcFrom(t1, seeds)
  }
}

// === Шаг системы ===
action step = any {
  { nondet i = oneOf(Ids) addTask(i) },

  { nondet n = oneOf(Ids) nondet c = oneOf(Ids) addTaskSibling(n, c) },
  { nondet n = oneOf(Ids) nondet c = oneOf(Ids) addBlockedSibling(n, c) },

  { nondet p = oneOf(Ids) nondet c = oneOf(Ids) addChildTask(p, c) },
  { nondet p = oneOf(Ids) nondet c = oneOf(Ids) addChild(p, c) },
  { nondet p = oneOf(Ids) nondet c = oneOf(Ids) deleteParentChild(p, c) },

  { nondet c = oneOf(Ids) nondet p = oneOf(Ids) addNewParent(c, p) },
  {
    nondet c = oneOf(Ids)
    nondet np = oneOf(Ids)
    nondet op = oneOf(Ids)
    moveTaskToNewParent(c, np, op)
  },

  { nondet b = oneOf(Ids) nondet d = oneOf(Ids) blockTask(b, d) },
  { nondet b = oneOf(Ids) nondet d = oneOf(Ids) unblockTask(b, d) },

  { nondet s = oneOf(Ids) nondet c = oneOf(Ids) cloneTask(s, c) },
  { nondet s = oneOf(Ids) nondet p = oneOf(Ids) nondet c = oneOf(Ids) cloneTaskIntoParent(s, p, c) },

  {
    nondet i = oneOf(Ids)
    nondet w = oneOf(Set(true, false))
    nondet r = oneOf(Set(true, false))
    nondet pb = oneOf(Set(true, false))
    nondet pe = oneOf(Set(true, false))
    updateMetadata(i, w, r, pb, pe)
  },

  {
    nondet i = oneOf(Ids)
    nondet st = oneOf(Set(Active, Completed, Archived))
    nondet c = oneOf(Ids)
    setCompletion(i, st, c)
  },

  { nondet i = oneOf(Ids) recalcAvailability(i) },
  { nondet i = oneOf(Ids) deleteTask(i) }
}

// === Инварианты ===
val parentChildSymmetry =
  tasks.keys().forall(p =>
    tasks.get(p).children.forall(c =>
      tasks.keys().contains(c) and tasks.get(c).parents.contains(p)))

val parentChildReverseSymmetry =
  tasks.keys().forall(c =>
    tasks.get(c).parents.forall(p =>
      tasks.keys().contains(p) and tasks.get(p).children.contains(c)))

val blockingSymmetry =
  tasks.keys().forall(b =>
    tasks.get(b).blocks.forall(d =>
      tasks.keys().contains(d) and tasks.get(d).blockedBy.contains(b)))

val blockingReverseSymmetry =
  tasks.keys().forall(d =>
    tasks.get(d).blockedBy.forall(b =>
      tasks.keys().contains(b) and tasks.get(b).blocks.contains(d)))

val availabilityMatchesRules =
  tasks.keys().forall(i =>
    tasks.get(i).canBeCompleted == expectedCanBeCompleted(i))

val completedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == Completed) implies tasks.get(i).hasCompletedTime)

val archivedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == Archived) implies tasks.get(i).hasArchiveTime)

val versionInitialized =
  tasks.keys().forall(i => tasks.get(i).version >= 1)

val noSelfRelations =
  tasks.keys().forall(i =>
    not(tasks.get(i).children.contains(i)) and
    not(tasks.get(i).parents.contains(i)) and
    not(tasks.get(i).blocks.contains(i)) and
    not(tasks.get(i).blockedBy.contains(i)))

val coreInvariants =
  noSelfRelations and
  parentChildSymmetry and
  parentChildReverseSymmetry and
  blockingSymmetry and
  blockingReverseSymmetry and
  availabilityMatchesRules and
  completedDatesConsistency and
  archivedDatesConsistency and
  versionInitialized

run exploration = init.then(12.reps(_ => step))

}

module unlimotion_desktop_integration {

/// Desktop-интеграционная модель:
/// - FileStorage + watcher/ignored ids;
/// - UnifiedTaskStorage init/migrations/event processing;
/// - TaskRelationsIndex rebuild после Init и мутаций задач;
/// - FileTaskMigrator (нормализация связей + версия);
/// - BackupViaGitService push/pull + ForceUpdateFile;
/// - TaskStorageFactory switch/create (file/server, watcher wiring);
/// - DependencyInjectionJobFactory contract (supported/unsupported jobs).

type TaskState = Active | Completed | Archived
type StorageMode = FileMode | ServerMode
type UpdateKind = Saved | Removed

type Task = {
  state: TaskState,
  canBeCompleted: bool,
  unlocked: bool,
  version: int,
  children: Set[str],
  parents: Set[str],
  blocks: Set[str],
  blockedBy: Set[str]
}

type DbUpdate = {
  id: str,
  kind: UpdateKind
}

type TaskMap = str -> Task
type RelationMap = str -> Set[str]

type SystemState = {
  tasks: TaskMap,
  relationsContains: RelationMap,
  relationsParents: RelationMap,
  relationsBlocks: RelationMap,
  relationsBlockedBy: RelationMap,
  storageMode: StorageMode,
  storageConnected: bool,
  watcherPresent: bool,
  watcherEnabled: bool,
  ignoredIds: Set[str],
  pendingUpdates: Set[DbUpdate],
  unifiedInitialized: bool,
  subscriptionsActive: bool,
  unifiedCache: Set[str],
  migrationVersion: int,
  availabilityReportCreated: bool,
  disconnectCount: int,
  backupPullCount: int,
  backupPushCount: int,
  jobPullRequests: int,
  jobPushRequests: int,
  jobUnknownRequests: int,
  jobPullCreated: int,
  jobPushCreated: int,
  jobUnknownThrows: int
}

pure val mkRawTask: Task = {
  state: Active,
  canBeCompleted: true,
  unlocked: false,
  version: 0,
  children: Set(),
  parents: Set(),
  blocks: Set(),
  blockedBy: Set()
}

pure val mkSavedTask: Task =
  mkRawTask.with("version", 1)

pure val RuntimeIds: Set[str] = Set("a", "b", "c", "d", "e", "f")
pure val RefIds: Set[str] = Set("a", "b", "c", "d", "e", "f", "ghost")

pure val initialState: SystemState = {
  tasks: Map(),
  relationsContains: Map(),
  relationsParents: Map(),
  relationsBlocks: Map(),
  relationsBlockedBy: Map(),
  storageMode: FileMode,
  storageConnected: true,
  watcherPresent: true,
  watcherEnabled: true,
  ignoredIds: Set(),
  pendingUpdates: Set(),
  unifiedInitialized: false,
  subscriptionsActive: false,
  unifiedCache: Set(),
  migrationVersion: 0,
  availabilityReportCreated: false,
  disconnectCount: 0,
  backupPullCount: 0,
  backupPushCount: 0,
  jobPullRequests: 0,
  jobPushRequests: 0,
  jobUnknownRequests: 0,
  jobPullCreated: 0,
  jobPushCreated: 0,
  jobUnknownThrows: 0
}

var system: SystemState

pure def hasTaskInState(s: SystemState, id: str): bool = s.tasks.keys().contains(id)

pure def relationContainsMapFromTasks(m: TaskMap): RelationMap =
  m.keys().fold(Map(), (acc, id) =>
    acc.put(id, m.get(id).children.intersect(m.keys())))

pure def relationParentsMapFromTasks(m: TaskMap): RelationMap =
  m.keys().fold(Map(), (acc, id) =>
    acc.put(id, m.get(id).parents.intersect(m.keys())))

pure def relationBlocksMapFromTasks(m: TaskMap): RelationMap =
  m.keys().fold(Map(), (acc, id) =>
    acc.put(id, m.get(id).blocks.intersect(m.keys())))

pure def relationBlockedByMapFromTasks(m: TaskMap): RelationMap =
  m.keys().fold(Map(), (acc, id) =>
    acc.put(id, m.get(id).blockedBy.intersect(m.keys())))

pure def rebuildRelationsInState(s: SystemState): SystemState =
  s
    .with("relationsContains", relationContainsMapFromTasks(s.tasks))
    .with("relationsParents", relationParentsMapFromTasks(s.tasks))
    .with("relationsBlocks", relationBlocksMapFromTasks(s.tasks))
    .with("relationsBlockedBy", relationBlockedByMapFromTasks(s.tasks))

pure def withTasksAndRebuiltRelations(s: SystemState, m: TaskMap): SystemState =
  rebuildRelationsInState(s.with("tasks", m))

pure def putTaskInState(s: SystemState, id: str, t: Task): SystemState =
  withTasksAndRebuiltRelations(s, s.tasks.put(id, t))

pure def setTaskInState(s: SystemState, id: str, t: Task): SystemState =
  withTasksAndRebuiltRelations(s, s.tasks.set(id, t))

pure def ensureTaskInState(s: SystemState, id: str): SystemState =
  if (hasTaskInState(s, id)) {
    s
  } else {
    putTaskInState(s, id, mkRawTask)
  }

pure def ensureSavedTaskInState(s: SystemState, id: str): SystemState =
  if (hasTaskInState(s, id)) {
    s
  } else {
    putTaskInState(s, id, mkSavedTask)
  }

pure def removeTaskMap(m: TaskMap, id: str): TaskMap =
  m.keys().exclude(Set(id)).fold(Map(), (acc, k) => acc.put(k, m.get(k)))

pure def dropTaskAndReferencesMap(m: TaskMap, id: str): TaskMap = {
  val remaining = m.keys().exclude(Set(id))
  remaining.fold(Map(), (acc, k) =>
    acc.put(
      k,
      m.get(k)
        .with("children", m.get(k).children.exclude(Set(id)))
        .with("parents", m.get(k).parents.exclude(Set(id)))
        .with("blocks", m.get(k).blocks.exclude(Set(id)))
        .with("blockedBy", m.get(k).blockedBy.exclude(Set(id)))
    ))
}

pure def savedUpdate(id: str): DbUpdate = { id: id, kind: Saved }
pure def removedUpdate(id: str): DbUpdate = { id: id, kind: Removed }

// ---------- Migration helpers (FileTaskMigrator + availability migration) ----------

pure def cleanForwardMap(m: TaskMap): TaskMap =
  m.keys().fold(m, (acc, id) =>
    acc.set(
      id,
      acc.get(id)
        .with("children", acc.get(id).children.intersect(m.keys()).exclude(Set(id)))
        .with("blocks", acc.get(id).blocks.intersect(m.keys()).exclude(Set(id)))
    ))

pure def rebuildParentsFromChildrenMap(m: TaskMap): TaskMap = {
  val cleared = m.keys().fold(m, (acc, id) => acc.set(id, acc.get(id).with("parents", Set())))
  m.keys().fold(cleared, (acc, p) =>
    acc.get(p).children.fold(acc, (a, c) =>
      a.set(c, a.get(c).with("parents", a.get(c).parents.union(Set(p)))))
  )
}

pure def rebuildBlockedByFromBlocksMap(m: TaskMap): TaskMap = {
  val cleared = m.keys().fold(m, (acc, id) => acc.set(id, acc.get(id).with("blockedBy", Set())))
  m.keys().fold(cleared, (acc, b) =>
    acc.get(b).blocks.fold(acc, (a, d) =>
      a.set(d, a.get(d).with("blockedBy", a.get(d).blockedBy.union(Set(b)))))
  )
}

pure def ensureVersionMap(m: TaskMap): TaskMap =
  m.keys().fold(m, (acc, id) =>
    acc.set(id, acc.get(id).with("version", if (acc.get(id).version < 1) { 1 } else { acc.get(id).version })))

pure def isDoneInMap(m: TaskMap, id: str): bool =
  m.get(id).state == Completed or m.get(id).state == Archived

pure def expectedCanBeCompletedInMap(m: TaskMap, id: str): bool =
  m.get(id).children.forall(c => not(m.keys().contains(c)) or isDoneInMap(m, c)) and
  m.get(id).blockedBy.forall(b => not(m.keys().contains(b)) or isDoneInMap(m, b))

pure def applyAvailability(t: Task, allowed: bool): Task =
  if (allowed) {
    {
      state: t.state,
      canBeCompleted: true,
      unlocked: true,
      version: t.version,
      children: t.children,
      parents: t.parents,
      blocks: t.blocks,
      blockedBy: t.blockedBy
    }
  } else {
    {
      state: t.state,
      canBeCompleted: false,
      unlocked: false,
      version: t.version,
      children: t.children,
      parents: t.parents,
      blocks: t.blocks,
      blockedBy: t.blockedBy
    }
  }

pure def recalcAllMap(m: TaskMap): TaskMap =
  m.keys().fold(m, (acc, id) =>
    acc.set(id, applyAvailability(acc.get(id), expectedCanBeCompletedInMap(acc, id))))

pure def normalizeReverseLinksMap(m: TaskMap): TaskMap =
  rebuildBlockedByFromBlocksMap(rebuildParentsFromChildrenMap(cleanForwardMap(m)))

pure def reverseAndVersionTasksMap(m: TaskMap): TaskMap =
  ensureVersionMap(normalizeReverseLinksMap(m))

pure def migrateReverseAndVersionInState(s: SystemState): SystemState =
  rebuildRelationsInState(
    s
      .with("tasks", reverseAndVersionTasksMap(s.tasks))
      .with("migrationVersion", 1))

pure def migrateAvailabilityInState(s: SystemState): SystemState =
  rebuildRelationsInState(
    s
      .with("tasks", recalcAllMap(s.tasks))
      .with("availabilityReportCreated", true))

// ---------- Actions ----------

action init = system' = initialState

// TaskStorageFactory create/switch
action createFileStorage = {
  val s1 =
    system
      .with("storageMode", FileMode)
      .with("storageConnected", true)
      .with("watcherPresent", true)
      .with("watcherEnabled", true)
      .with("ignoredIds", Set())
      .with("pendingUpdates", Set())
      .with("unifiedInitialized", false)
      .with("subscriptionsActive", false)
      .with("unifiedCache", Set())
  system' = s1
}

action createServerStorage = {
  val s1 =
    system
      .with("storageMode", ServerMode)
      .with("storageConnected", true)
      .with("watcherPresent", false)
      .with("watcherEnabled", false)
      .with("ignoredIds", Set())
      .with("pendingUpdates", Set())
      .with("unifiedInitialized", false)
      .with("subscriptionsActive", false)
      .with("unifiedCache", Set())
  system' = s1
}

action switchToFileStorage = {
  val base = system.with("disconnectCount", system.disconnectCount + 1)
  val s1 =
    base
      .with("storageMode", FileMode)
      .with("storageConnected", true)
      .with("watcherPresent", true)
      .with("watcherEnabled", true)
      .with("ignoredIds", Set())
      .with("pendingUpdates", Set())
      .with("unifiedInitialized", false)
      .with("subscriptionsActive", false)
      .with("unifiedCache", Set())
  system' = s1
}

action switchToServerStorage = {
  val base = system.with("disconnectCount", system.disconnectCount + 1)
  val s1 =
    base
      .with("storageMode", ServerMode)
      .with("storageConnected", true)
      .with("watcherPresent", false)
      .with("watcherEnabled", false)
      .with("ignoredIds", Set())
      .with("pendingUpdates", Set())
      .with("unifiedInitialized", false)
      .with("subscriptionsActive", false)
      .with("unifiedCache", Set())
  system' = s1
}

// Raw file-level edits (pre-migration / external)
action rawUpsert(id) = all {
  not(system.unifiedInitialized),
  system' = ensureTaskInState(system, id)
}

action rawSetState(id, st) = all {
  not(system.unifiedInitialized),
  hasTaskInState(system, id),
  system' = setTaskInState(system, id, system.tasks.get(id).with("state", st))
}

action rawAddChildRef(parent, childRef) = all {
  not(system.unifiedInitialized),
  hasTaskInState(system, parent),
  system' = setTaskInState(system, parent,
    system.tasks.get(parent).with("children", system.tasks.get(parent).children.union(Set(childRef))))
}

action rawAddBlockRef(blocking, blockedRef) = all {
  not(system.unifiedInitialized),
  hasTaskInState(system, blocking),
  system' = setTaskInState(system, blocking,
    system.tasks.get(blocking).with("blocks", system.tasks.get(blocking).blocks.union(Set(blockedRef))))
}

action rawAddParentRef(child, parentRef) = all {
  not(system.unifiedInitialized),
  hasTaskInState(system, child),
  system' = setTaskInState(system, child,
    system.tasks.get(child).with("parents", system.tasks.get(child).parents.union(Set(parentRef))))
}

action rawAddBlockedByRef(blocked, blockerRef) = all {
  not(system.unifiedInitialized),
  hasTaskInState(system, blocked),
  system' = setTaskInState(system, blocked,
    system.tasks.get(blocked).with("blockedBy", system.tasks.get(blocked).blockedBy.union(Set(blockerRef))))
}

// FileStorage behavior (save/remove + ignored ids)
action fileSave(id) = {
  val s1 = ensureSavedTaskInState(system, id)
  all {
    system.storageMode == FileMode,
    system.storageConnected,
    system' = s1.with("ignoredIds", s1.ignoredIds.union(Set(id)))
  }
}

action fileRemove(id) = {
  val t1 = dropTaskAndReferencesMap(system.tasks, id)
  val recalced = recalcAllMap(t1)
  all {
    system.storageMode == FileMode,
    system.storageConnected,
    hasTaskInState(system, id),
    system' = withTasksAndRebuiltRelations(system, recalced)
      .with("unifiedCache", system.unifiedCache.exclude(Set(id)))
      .with("ignoredIds", system.ignoredIds.union(Set(id)))
  }
}

action expireIgnored(id) = all {
  system.ignoredIds.contains(id),
  system' = system.with("ignoredIds", system.ignoredIds.exclude(Set(id)))
}

// FileDbWatcher behavior
action watcherExternalSaved(id) = all {
  system.storageMode == FileMode,
  system.watcherPresent,
  system.watcherEnabled,
  not(system.ignoredIds.contains(id)),
  system' = system.with("pendingUpdates", system.pendingUpdates.union(Set(savedUpdate(id))))
}

action watcherExternalRemoved(id) = all {
  system.storageMode == FileMode,
  system.watcherPresent,
  system.watcherEnabled,
  not(system.ignoredIds.contains(id)),
  system' = system.with("pendingUpdates", system.pendingUpdates.union(Set(removedUpdate(id))))
}

// .lock file gets normalized to target id in FileDbWatcher
action watcherExternalLockSaved(id) = all {
  system.storageMode == FileMode,
  system.watcherPresent,
  system.watcherEnabled,
  not(system.ignoredIds.contains(id)),
  system' = system.with("pendingUpdates", system.pendingUpdates.union(Set(savedUpdate(id))))
}

// BackupViaGitService uses ForceUpdateFile after merge, bypassing watcher enabled flag
action watcherForceSaved(id) = all {
  system.watcherPresent,
  system' = system.with("pendingUpdates", system.pendingUpdates.union(Set(savedUpdate(id))))
}

action watcherForceRemoved(id) = all {
  system.watcherPresent,
  system' = system.with("pendingUpdates", system.pendingUpdates.union(Set(removedUpdate(id))))
}

// BackupViaGitService push/pull wrappers
action backupPush = all {
  system.storageMode == FileMode,
  system.watcherPresent,
  system' = system
    .with("watcherEnabled", true)
    .with("backupPushCount", system.backupPushCount + 1)
}

action backupPullSaved(id) = {
  val s1 = ensureSavedTaskInState(system, id)
  all {
    system.storageMode == FileMode,
    system.watcherPresent,
    system' = s1
      .with("watcherEnabled", true)
      .with("backupPullCount", s1.backupPullCount + 1)
      .with("pendingUpdates", s1.pendingUpdates.union(Set(savedUpdate(id))))
  }
}

action backupPullRemoved(id) = {
  val t1 = dropTaskAndReferencesMap(system.tasks, id)
  all {
    system.storageMode == FileMode,
    system.watcherPresent,
    system' = withTasksAndRebuiltRelations(system, t1)
      .with("watcherEnabled", true)
      .with("backupPullCount", system.backupPullCount + 1)
      .with("pendingUpdates", system.pendingUpdates.union(Set(removedUpdate(id))))
  }
}

// UnifiedTaskStorage init and event handling
action unifiedInit = {
  val reverseWillChange =
    system.storageMode == FileMode and reverseAndVersionTasksMap(system.tasks) != system.tasks

  val afterReverse =
    if (system.storageMode == FileMode) {
      migrateReverseAndVersionInState(system)
    } else {
      system
    }

  val shouldRunAvailability =
    afterReverse.storageMode == FileMode and
    ((not(afterReverse.availabilityReportCreated)) or reverseWillChange)

  val afterAvailability =
    if (shouldRunAvailability) {
      migrateAvailabilityInState(afterReverse)
    } else {
      afterReverse
    }

  val afterRelations = rebuildRelationsInState(afterAvailability)

  all {
    afterRelations.storageConnected,
    not(afterRelations.unifiedInitialized),
    system' = afterRelations
      .with("unifiedInitialized", true)
      .with("subscriptionsActive", true)
      .with("unifiedCache", afterRelations.tasks.keys())
  }
}

action processSavedUpdate(id) = {
  val ev = savedUpdate(id)
  val cache1 =
    if (system.tasks.keys().contains(id)) {
      system.unifiedCache.union(Set(id))
    } else {
      system.unifiedCache
    }
  all {
    system.subscriptionsActive,
    system.pendingUpdates.contains(ev),
    system' = system
      .with("pendingUpdates", system.pendingUpdates.exclude(Set(ev)))
      .with("unifiedCache", cache1)
  }
}

action processRemovedUpdate(id) = {
  val ev = removedUpdate(id)
  val dropped = dropTaskAndReferencesMap(system.tasks, id)
  val recalced =
    if (system.storageMode == FileMode) {
      recalcAllMap(dropped)
    } else {
      dropped
    }
  all {
    system.subscriptionsActive,
    system.pendingUpdates.contains(ev),
    system' = withTasksAndRebuiltRelations(system, recalced)
      .with("pendingUpdates", system.pendingUpdates.exclude(Set(ev)))
      .with("unifiedCache", system.unifiedCache.exclude(Set(id)))
  }
}

// DependencyInjectionJobFactory contract
action requestGitPullJob =
  system' = system
    .with("jobPullRequests", system.jobPullRequests + 1)
    .with("jobPullCreated", system.jobPullCreated + 1)

action requestGitPushJob =
  system' = system
    .with("jobPushRequests", system.jobPushRequests + 1)
    .with("jobPushCreated", system.jobPushCreated + 1)

action requestUnknownJob =
  system' = system
    .with("jobUnknownRequests", system.jobUnknownRequests + 1)
    .with("jobUnknownThrows", system.jobUnknownThrows + 1)

action step = any {
  { createFileStorage },
  { createServerStorage },
  { switchToFileStorage },
  { switchToServerStorage },

  { unifiedInit },

  { nondet i = oneOf(RuntimeIds) rawUpsert(i) },
  { nondet i = oneOf(RuntimeIds) nondet st = oneOf(Set(Active, Completed, Archived)) rawSetState(i, st) },
  { nondet p = oneOf(RuntimeIds) nondet c = oneOf(RefIds) rawAddChildRef(p, c) },
  { nondet b = oneOf(RuntimeIds) nondet d = oneOf(RefIds) rawAddBlockRef(b, d) },
  { nondet c = oneOf(RuntimeIds) nondet p = oneOf(RefIds) rawAddParentRef(c, p) },
  { nondet d = oneOf(RuntimeIds) nondet b = oneOf(RefIds) rawAddBlockedByRef(d, b) },

  { nondet i = oneOf(RuntimeIds) fileSave(i) },
  { nondet i = oneOf(RuntimeIds) fileRemove(i) },
  { nondet i = oneOf(RuntimeIds) expireIgnored(i) },

  { nondet i = oneOf(RuntimeIds) watcherExternalSaved(i) },
  { nondet i = oneOf(RuntimeIds) watcherExternalRemoved(i) },
  { nondet i = oneOf(RuntimeIds) watcherExternalLockSaved(i) },
  { nondet i = oneOf(RuntimeIds) watcherForceSaved(i) },
  { nondet i = oneOf(RuntimeIds) watcherForceRemoved(i) },

  { backupPush },
  { nondet i = oneOf(RuntimeIds) backupPullSaved(i) },
  { nondet i = oneOf(RuntimeIds) backupPullRemoved(i) },

  { nondet i = oneOf(RuntimeIds) processSavedUpdate(i) },
  { nondet i = oneOf(RuntimeIds) processRemovedUpdate(i) },

  { requestGitPullJob },
  { requestGitPushJob },
  { requestUnknownJob }
}

// ---------- Invariants ----------

val stableFileStateAfterInit =
  system.unifiedInitialized and system.storageMode == FileMode and system.pendingUpdates == Set()

val noSelfRelationsAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(i =>
    not(system.tasks.get(i).children.contains(i)) and
    not(system.tasks.get(i).parents.contains(i)) and
    not(system.tasks.get(i).blocks.contains(i)) and
    not(system.tasks.get(i).blockedBy.contains(i)))

val parentChildSymmetryAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(p =>
    system.tasks.get(p).children.forall(c =>
      system.tasks.keys().contains(c) and system.tasks.get(c).parents.contains(p)))

val parentChildReverseSymmetryAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(c =>
    system.tasks.get(c).parents.forall(p =>
      system.tasks.keys().contains(p) and system.tasks.get(p).children.contains(c)))

val blockingSymmetryAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(b =>
    system.tasks.get(b).blocks.forall(d =>
      system.tasks.keys().contains(d) and system.tasks.get(d).blockedBy.contains(b)))

val blockingReverseSymmetryAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(d =>
    system.tasks.get(d).blockedBy.forall(b =>
      system.tasks.keys().contains(b) and system.tasks.get(b).blocks.contains(d)))

val availabilityMatchesRulesAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(i =>
    system.tasks.get(i).canBeCompleted == expectedCanBeCompletedInMap(system.tasks, i))

val versionsInitializedAfterInit =
  (not(stableFileStateAfterInit)) or
  system.tasks.keys().forall(i => system.tasks.get(i).version >= 1)

val relationContainsMatchesTasks =
  system.relationsContains == relationContainsMapFromTasks(system.tasks)

val relationParentsMatchesTasks =
  system.relationsParents == relationParentsMapFromTasks(system.tasks)

val relationBlocksMatchesTasks =
  system.relationsBlocks == relationBlocksMapFromTasks(system.tasks)

val relationBlockedByMatchesTasks =
  system.relationsBlockedBy == relationBlockedByMapFromTasks(system.tasks)

val relationSnapshotConsistent =
  relationContainsMatchesTasks and
  relationParentsMatchesTasks and
  relationBlocksMatchesTasks and
  relationBlockedByMatchesTasks

val cacheSubsetOfTasks =
  system.unifiedCache.forall(i =>
    system.tasks.keys().contains(i) or system.pendingUpdates.contains(removedUpdate(i)))

val subscriptionsRequireInit =
  system.subscriptionsActive implies system.unifiedInitialized

val watcherConsistentWithStorageMode =
  (system.storageMode == FileMode) == system.watcherPresent

val watcherEnabledWhenPresent =
  (not(system.watcherPresent)) or system.watcherEnabled

val pendingUpdatesRequireWatcher =
  system.pendingUpdates.forall(_ => system.watcherPresent)

val migrationsAppliedWhenUnifiedInitialized =
  (system.storageMode == FileMode and system.unifiedInitialized) implies
  (system.migrationVersion >= 1 and system.availabilityReportCreated)

val jobFactoryContract =
  system.jobUnknownRequests == system.jobUnknownThrows and
  system.jobPullRequests == system.jobPullCreated and
  system.jobPushRequests == system.jobPushCreated

val desktopCoreInvariants =
  noSelfRelationsAfterInit and
  parentChildSymmetryAfterInit and
  parentChildReverseSymmetryAfterInit and
  blockingSymmetryAfterInit and
  blockingReverseSymmetryAfterInit and
  availabilityMatchesRulesAfterInit and
  versionsInitializedAfterInit and
  relationSnapshotConsistent and
  cacheSubsetOfTasks and
  subscriptionsRequireInit and
  watcherConsistentWithStorageMode and
  watcherEnabledWhenPresent and
  pendingUpdatesRequireWatcher and
  migrationsAppliedWhenUnifiedInitialized and
  jobFactoryContract

run desktopExploration = init.then(20.reps(_ => step))

}
