module unlimotion_file_mode

/// Quint-спецификация файлового режима Unlimotion.
/// Модель отражает только локальное (FileStorage) поведение без server-mode.

import std

// === Доменные типы ===
type TaskState = "active" | "completed" | "archived"

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

pure def mkTask(): Task = {
  state: "active",
  canBeCompleted: true,
  unlocked: false,
  hasCompletedTime: false,
  hasArchiveTime: false,
  children: Set(),
  parents: Set(),
  blocks: Set(),
  blockedBy: Set()
}

var tasks: str -> Task

// === Предикаты и производные вычисления ===
pure def exists(id) = tasks.contains(id)

pure def isDone(id) =
  tasks.get(id).state == "completed" or tasks.get(id).state == "archived"

pure def expectedCanBeCompleted(id) =
  tasks.get(id).children.forall(c => exists(c) and isDone(c)) and
  tasks.get(id).blockedBy.forall(b => exists(b) and isDone(b))

pure def withAvailability(t, allowed) =
  if (allowed) {
    t.with({ canBeCompleted: true, unlocked: true })
  } else {
    t.with({ canBeCompleted: false, unlocked: false })
  }

action recalcAvailability(id) = all {
  exists(id),
  val allowed = expectedCanBeCompleted(id),
  tasks' = tasks.set(id, withAvailability(tasks.get(id), allowed))
}

// === Операции, соответствующие TaskTreeManager в file-mode ===
action addTask(id) = all {
  not(exists(id)),
  tasks' = tasks.set(id, mkTask())
}

action addChild(parent, child) = all {
  exists(parent),
  exists(child),
  parent != child,
  val p1 = tasks.get(parent).with({ children: tasks.get(parent).children.union(Set(child)) }),
  val c1 = tasks.get(child).with({ parents: tasks.get(child).parents.union(Set(parent)) }),
  val t1 = tasks.set(parent, p1).set(child, c1),
  val pAllowed = p1.children.forall(c => t1.contains(c) and (t1.get(c).state != "active")) and
                 p1.blockedBy.forall(b => t1.contains(b) and (t1.get(b).state != "active")),
  tasks' = t1.set(parent, withAvailability(p1, pAllowed))
}

action blockTask(blocking, blocked) = all {
  exists(blocking),
  exists(blocked),
  blocking != blocked,
  val b1 = tasks.get(blocking).with({ blocks: tasks.get(blocking).blocks.union(Set(blocked)) }),
  val d1 = tasks.get(blocked).with({ blockedBy: tasks.get(blocked).blockedBy.union(Set(blocking)) }),
  val t1 = tasks.set(blocking, b1).set(blocked, d1),
  val dAllowed = d1.children.forall(c => t1.contains(c) and (t1.get(c).state != "active")) and
                 d1.blockedBy.forall(b => t1.contains(b) and (t1.get(b).state != "active")),
  tasks' = t1.set(blocked, withAvailability(d1, dAllowed))
}

action unblockTask(blocking, blocked) = all {
  exists(blocking),
  exists(blocked),
  val b1 = tasks.get(blocking).with({ blocks: tasks.get(blocking).blocks.exclude(blocked) }),
  val d1 = tasks.get(blocked).with({ blockedBy: tasks.get(blocked).blockedBy.exclude(blocking) }),
  val t1 = tasks.set(blocking, b1).set(blocked, d1),
  val dAllowed = d1.children.forall(c => t1.contains(c) and (t1.get(c).state != "active")) and
                 d1.blockedBy.forall(b => t1.contains(b) and (t1.get(b).state != "active")),
  tasks' = t1.set(blocked, withAvailability(d1, dAllowed))
}

action setCompletion(id, state) = all {
  exists(id),
  (state == "active" or state == "completed" or state == "archived"),
  val old = tasks.get(id),
  val withDates =
    if (state == "completed") {
      old.with({ state: "completed", hasCompletedTime: true, hasArchiveTime: false })
    } else if (state == "active") {
      old.with({ state: "active", hasCompletedTime: false, hasArchiveTime: false })
    } else {
      old.with({ state: "archived", hasCompletedTime: false, hasArchiveTime: true })
    },
  val t1 = tasks.set(id, withDates),
  val selfAllowed = withDates.children.forall(c => t1.contains(c) and (t1.get(c).state != "active")) and
                    withDates.blockedBy.forall(b => t1.contains(b) and (t1.get(b).state != "active")),
  tasks' = t1.set(id, withAvailability(withDates, selfAllowed))
}

action deleteTask(id) = all {
  exists(id),
  // Удаляем симметричные ссылки у всех связанных задач.
  val t1 = tasks.keys().foldl(tasks, (acc, k) =>
    if (k == id) {
      acc
    } else {
      val tk = acc.get(k)
      acc.set(k, tk.with({
        children: tk.children.exclude(id),
        parents: tk.parents.exclude(id),
        blocks: tk.blocks.exclude(id),
        blockedBy: tk.blockedBy.exclude(id)
      }))
    }),
  tasks' = t1.remove(id)
}

// === Инициализация и шаг системы ===
action init = tasks' = Map()

action step =
  exists(i) and recalcAvailability(i) or
  addTask(i) or
  (exists(p) and exists(c) and addChild(p, c)) or
  (exists(b) and exists(d) and blockTask(b, d)) or
  (exists(b) and exists(d) and unblockTask(b, d)) or
  (exists(i) and (s == "active" or s == "completed" or s == "archived") and setCompletion(i, s)) or
  (exists(i) and deleteTask(i))

// === Инварианты для проверки ===
val parentChildSymmetry =
  tasks.keys().forall(p =>
    tasks.get(p).children.forall(c =>
      tasks.contains(c) and tasks.get(c).parents.contains(p)))

val blockingSymmetry =
  tasks.keys().forall(b =>
    tasks.get(b).blocks.forall(d =>
      tasks.contains(d) and tasks.get(d).blockedBy.contains(b)))

val availabilityMatchesRules =
  tasks.keys().forall(i =>
    tasks.get(i).canBeCompleted == expectedCanBeCompleted(i))

val completedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == "completed") implies tasks.get(i).hasCompletedTime)

val archivedDatesConsistency =
  tasks.keys().forall(i =>
    (tasks.get(i).state == "archived") implies tasks.get(i).hasArchiveTime)

val invariantsHold =
  parentChildSymmetry and
  blockingSymmetry and
  availabilityMatchesRules and
  completedDatesConsistency and
  archivedDatesConsistency
