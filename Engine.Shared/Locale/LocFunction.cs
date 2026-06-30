using System;
using System.Collections.Generic;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;

namespace Engine.Shared.Locale;

// have a better look than the linguini default one
// does not have any currentl effect, could be completed removed, but i wanted this because in this way i could have a 
// simpler and future proof version
public static class LocFunction
{
    public delegate IFluentType FluentMethod(
        IList<IFluentType> args,
        IDictionary<string, IFluentType> named);

    public static ExternalFunction Wrap(FluentMethod method)
    {
        return (args, named) =>
        {
            return method(args, named);
        };
    }
}
