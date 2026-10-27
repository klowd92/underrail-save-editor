using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// A service provider that answers any interface with an inert stub. The game's UI code (tooltips) asks for
    /// services that only exist in a running game, such as interface-size settings, to pick fonts. Installed as the
    /// game's fallback provider only while such code runs, so rules code never silently gets a stub.
    /// </summary>
    internal sealed class StubServiceProvider : IServiceProvider
    {
        private readonly ConcurrentDictionary<Type, object> _stubs = new ConcurrentDictionary<Type, object>();

        public object GetService(Type serviceType) =>
            serviceType.IsInterface ? _stubs.GetOrAdd(serviceType, t => new InertStub(t, this).GetTransparentProxy()) : null;

        private sealed class InertStub : RealProxy
        {
            private readonly StubServiceProvider _owner;
            private readonly ConcurrentDictionary<MethodBase, object> _results = new ConcurrentDictionary<MethodBase, object>();

            public InertStub(Type type, StubServiceProvider owner) : base(type)
            {
                _owner = owner;
            }

            public override IMessage Invoke(IMessage msg)
            {
                var call = (IMethodCallMessage)msg;
                var result = _results.GetOrAdd(call.MethodBase, m => Default(((MethodInfo)m).ReturnType));
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }

            /// <summary>Defaults all the way down: interfaces get stubs, plain classes a new instance, values their zero.</summary>
            private object Default(Type type)
            {
                if (type == typeof(void))
                    return null;
                if (type == typeof(string))
                    return "";
                if (type.IsValueType)
                    return Activator.CreateInstance(type);
                if (type.IsInterface)
                    return _owner.GetService(type);
                if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    try { return Activator.CreateInstance(type); }
                    catch (Exception) { return null; }
                }
                return null;
            }
        }
    }
}
