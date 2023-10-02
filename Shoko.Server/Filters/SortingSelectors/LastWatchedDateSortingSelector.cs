using System;
using Shoko.Server.Filters.Interfaces;

namespace Shoko.Server.Filters.SortingSelectors;

public class LastWatchedDateSortingSelector : UserDependentSortingExpression
{
    public override bool TimeDependent => false;
    public override bool UserDependent => true;
    public override string HelpDescription => "This sorts by the last date that a filterable was watched by the current user";
    public DateTime DefaultValue { get; set; }

    public override object Evaluate(IUserDependentFilterable f)
    {
        return f.LastWatchedDate ?? DefaultValue;
    }
}