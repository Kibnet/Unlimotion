module unlimotion_file_mode {

/// Quint-спецификация файлового режима Unlimotion.
/// Модель отражает только локальное (FileStorage) поведение без server-mode.

// === Доменные типы ===
type TaskState = Active | Completed | Archived

type Task = {
  state: TaskState,
  canBeCompleted: bool,
  unlocked: bool,
  hasCompletedTime: bool,
  hasArchiveTime: bool,
  children: Set[str],
  parents: Set[str],
  blocks: Set[str],
  blockedBy: Set[str]
}

pure val mkTask: Task = {
  state: Active,
  canBeCompleted: true,
  unlocked: false,
  hasCompletedTime: false,
  hasArchiveTime: false,
  children: Set(),
  parents: Set(),
  blocks: Set(),
  blockedBy: Set()
}

pure val Ids: Set[str] = Set("a", "b", "c", "d")

var tasks: str -> Task

// === Предикаты и производные вычисления ===
def hasTask(id) = tasks.keys().contains(id)

def isDone(id) =
  isDoneIn(tasks, id)

def expectedCanBeCompleted(id) =
  expectedCanBeCompletedIn(tasks, id)

pure def withAvailability(t, allowed) =
  if (allowed) {
    t.with("canBeCompleted", true).with("unlocked", true)
  } else {
    t.with("canBeCompleted", false).with("unlocked", false)
  }

pure def hasTaskIn(m, id) = m.keys().contains(id)

pure def isDoneIn(m, id) =
  m.get(id).state == Completed or m.get(id).state == Archived

pure def expectedCanBeCompletedIn(m, id) =
  m.get(id).children.forall(c => hasTaskIn(m, c) and isDoneIn(m, c)) and
  m.get(id).blockedBy.forall(b => hasTaskIn(m, b) and isDoneIn(m, b))

pure def normalizeAvailability(m) =
  m.keys().fold(m, (acc, i) =>
    acc.set(i, withAvailability(acc.get(i), expectedCanBeCompletedIn(m, i))))

action recalcAvailability(id) = all {
  hasTask(id),
  tasks' = normalizeAvailability(tasks)
}

// === Операции, соответствующие TaskTreeManager в file-mode ===
action addTask(id) = all {
  not(hasTask(id)),
  tasks' = normalizeAvailability(tasks.put(id, mkTask))
}

action addChild(parent, child) = {
  val p1 = tasks.get(parent).with("children", tasks.get(parent).children.union(Set(child)))
  val c1 = tasks.get(child).with("parents", tasks.get(child).parents.union(Set(parent)))
  val t1 = tasks.set(parent, p1).set(child, c1)
  all {
    hasTask(parent),
    hasTask(child),
    parent != child,
    tasks' = normalizeAvailability(t1)
  }
}

action blockTask(blocking, blocked) = {
  val b1 = tasks.get(blocking).with("blocks", tasks.get(blocking).blocks.union(Set(blocked)))
  val d1 = tasks.get(blocked).with("blockedBy", tasks.get(blocked).blockedBy.union(Set(blocking)))
  val t1 = tasks.set(blocking, b1).set(blocked, d1)
  all {
    hasTask(blocking),
    hasTask(blocked),
    blocking != blocked,
    tasks' = normalizeAvailability(t1)
  }
}

action unblockTask(blocking, blocked) = {
  val b1 = tasks.get(blocking).with("blocks", tasks.get(blocking).blocks.exclude(Set(blocked)))
  val d1 = tasks.get(blocked).with("blockedBy", tasks.get(blocked).blockedBy.exclude(Set(blocking)))
  val t1 = tasks.set(blocking, b1).set(blocked, d1)
  all {
    hasTask(blocking),
    hasTask(blocked),
    tasks' = normalizeAvailability(t1)
  }
}

action setCompletion(id, newState) = {
  val old = tasks.get(id)
  val withDates =
    if (newState == Completed) {
      old.with("state", Completed).with("hasCompletedTime", true).with("hasArchiveTime", false)
    } else if (newState == Active) {
      old.with("state", Active).with("hasCompletedTime", false).with("hasArchiveTime", false)
    } else {
      old.with("state", Archived).with("hasCompletedTime", false).with("hasArchiveTime", true)
    }
  val t1 = tasks.set(id, withDates)
  all {
    hasTask(id),
    (newState == Active or newState == Completed or newState == Archived),
    tasks' = normalizeAvailability(t1)
  }
}

action deleteTask(id) = {
  // Удаляем задачу через пересборку map без id и чистим симметричные ссылки.
  val remaining = tasks.keys().exclude(Set(id))
  all {
    hasTask(id),
    tasks' = normalizeAvailability(
      remaining.fold(Map(), (acc, k) =>
        acc.put(
          k,
          tasks.get(k)
            .with("children", tasks.get(k).children.exclude(Set(id)))
            .with("parents", tasks.get(k).parents.exclude(Set(id)))
            .with("blocks", tasks.get(k).blocks.exclude(Set(id)))
            .with("blockedBy", tasks.get(k).blockedBy.exclude(Set(id)))
        )))
  }
}

// === Инициализация и шаг системы ===
action init = tasks' = Map()

action step = any {
  { nondet i = oneOf(Ids) addTask(i) },
  { nondet i = oneOf(Ids) recalcAvailability(i) },
  {
    nondet p = oneOf(Ids)
    nondet c = oneOf(Ids)
    addChild(p, c)
  },
  {
    nondet b = oneOf(Ids)
    nondet d = oneOf(Ids)
    blockTask(b, d)
  },
  {
    nondet b = oneOf(Ids)
    nondet d = oneOf(Ids)
    unblockTask(b, d)
  },
  {
    nondet i = oneOf(Ids)
    nondet s = oneOf(Set(Active, Completed, Archived))
    setCompletion(i, s)
  },
  { nondet i = oneOf(Ids) deleteTask(i) }
}

// === Инварианты для проверки ===
val parentChildSymmetry =
  tasks.keys().forall(p =>
    tasks.get(p).children.forall(c =>
      tasks.keys().contains(c) and tasks.get(c).parents.contains(p)))

val blockingSymmetry =
  tasks.keys().forall(b =>
    tasks.get(b).blocks.forall(d =>
      tasks.keys().contains(d) and tasks.get(d).blockedBy.contains(b)))

val availabilityMatchesRules =
  tasks.keys().forall(i =>
    tasks.get(i).canBeCompleted == expectedCanBeCompleted(i))

val completedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == Completed) implies tasks.get(i).hasCompletedTime)

val archivedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == Archived) implies tasks.get(i).hasArchiveTime)

val invariantsHold =
  parentChildSymmetry and
  blockingSymmetry and
  availabilityMatchesRules and
  completedDatesConsistency and
  archivedDatesConsistency

run smoke = init.then(8.reps(_ => step)).expect(invariantsHold)

}
