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
