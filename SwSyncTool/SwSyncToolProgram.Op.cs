using System;
using SysWeaver;
using System.Threading.Tasks;

namespace SwSyncTool
{
    static partial class SwSyncToolProgram
    {
        sealed class Op
        {
            public readonly String Name;
            public readonly int MinArgs;
            public readonly int MaxArgs;
            public readonly String Description;
            public Func<String[], CdcProps, Params, ValueTask<int>> Func;

            public Op(Func<String[], CdcProps, Params, ValueTask<int>> func, string name, string description, int minArgs = 0, int maxArgs = 0)
            {
                Func = func;
                Name = name;
                Description = description;
                MinArgs = minArgs;
                MaxArgs = maxArgs < minArgs ? minArgs : maxArgs;
            }
        }

#pragma warning restore CS0649

    }



}
