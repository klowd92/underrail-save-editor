using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// Stands in for the game window's host service. Every string-returning member answers with the game's
    /// content root (the data folder), which is what item definitions are loaded from. Graphics members return null.
    /// </summary>
    internal sealed class HostServiceStub : RealProxy
    {
        private readonly string _contentRoot;

        public HostServiceStub(Type serviceInterface, string contentRoot) : base(serviceInterface)
        {
            _contentRoot = contentRoot;
        }

        public override IMessage Invoke(IMessage msg)
        {
            var call = (IMethodCallMessage)msg;
            var returnType = ((System.Reflection.MethodInfo)call.MethodBase).ReturnType;
            object result;
            if (returnType == typeof(string))
                result = _contentRoot;
            else if (returnType == typeof(TimeSpan))
                result = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
            else if (returnType.IsValueType && returnType != typeof(void))
                result = Activator.CreateInstance(returnType);
            else
                result = null;
            return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
        }
    }
}
