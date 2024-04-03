using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Test
{
    public class InputTestData
    {
        public InputTestData(TaskListTypes listType, 
               List<TaskItem> expectedListItems)
        {
            ListType = listType;
            ExpectedListItems = expectedListItems;           
        }
        public TaskListTypes ListType { get; set; }
        public List<TaskItem> ExpectedListItems { get; set; }        
    }
}
