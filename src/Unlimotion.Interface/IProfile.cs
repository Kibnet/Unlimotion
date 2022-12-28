using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Unlimotion.Interface
{
    public interface IProfile
    {
        Task Open(string userId);
    }
}
