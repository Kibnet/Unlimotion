using System;

namespace Unlimotion.ViewModel.Models
{
    public class UpdatedTask{
        public DateTime UpdatedDateTime { get; }
        /// <summary>
        /// В случае с таском, полученным из файла Id - полный путь к файлу<br/>
        /// В случае с таском, полученным из Базы Данных, Id - это идентификатор записи в БД
        /// </summary>
        public string Id { get; }
        public UpdatingTaskType UpdatingType { get; }
        public UpdatedTask(string id, UpdatingTaskType updatingType) {
            Id = id;
            UpdatingType = updatingType;
            UpdatedDateTime = DateTime.Now;
        }

        public UpdatedTask(string id, UpdatingTaskType updatingType, DateTime updatedDateTime) {
            Id = id;
            UpdatingType = updatingType;
            UpdatedDateTime = updatedDateTime;
        }

        public override int GetHashCode() {
            return Id.GetHashCode();
        }
    }
}