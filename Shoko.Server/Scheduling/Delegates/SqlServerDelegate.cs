using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Quartz.Impl.AdoJobStore;
using Quartz.Spi;
using Quartz.Util;

namespace Shoko.Server.Scheduling.Delegates;

public class SqlServerDelegate : Quartz.Impl.AdoJobStore.SqlServerDelegate, IFilteredDriverDelegate
{
    private string _schedulerName;

    private string[] GetJobClasses(IEnumerable<Type> types) => types.Select(GetStorableJobTypeName).ToArray();

    private string GetSelectPartNoExclusions()
    {
        return @$"SELECT t.{ColumnTriggerName}, t.{ColumnTriggerGroup}, jd.{ColumnJobClass}, t.{ColumnPriority}, t.{ColumnNextFireTime}
              FROM {TablePrefixSubst}{TableTriggers} t
              JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))";
    }

    private string GetSelectPartExcludingTypes()
    {
        return @$"SELECT t.{ColumnTriggerName}, t.{ColumnTriggerGroup}, jd.{ColumnJobClass}, t.{ColumnPriority}, t.{ColumnNextFireTime}
              FROM {TablePrefixSubst}{TableTriggers} t
              JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))
                AND jd.{ColumnJobClass} NOT IN (@types)";
    }

    private string GetSelectPartLimitType(int index)
    {
        return @$"SELECT TOP @limit{index} t.{ColumnTriggerName}, t.{ColumnTriggerGroup}, jd.{ColumnJobClass}, t.{ColumnPriority}, t.{ColumnNextFireTime}
              FROM {TablePrefixSubst}{TableTriggers} t
              JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))
                AND jd.{ColumnJobClass} IN (@limit{index}Types)";
    }

    private const string SelectBlockedTriggerCountSql= @$"SELECT COUNT(1)
              FROM {TablePrefixSubst}{TableTriggers} t
              JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE t.{ColumnSchedulerName} = @schedulerName AND (({ColumnTriggerState} = '{StateWaiting}' AND jd.{ColumnJobClass} IN (@types)) OR {ColumnTriggerState} = '{StateBlocked}') AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))";

    const string UpdateJobTriggerStatesFromOtherStateSql = @$"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state
               FROM {TablePrefixSubst}{TableTriggers} t
               INNER JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName})
               WHERE t.{ColumnSchedulerName} = @schedulerName AND t.{ColumnTriggerState} = @oldState AND jd.{ColumnJobClass} IN (@types)";

    private const string SelectJobClassesAndCountSql= @$"SELECT jd.{ColumnJobClass}, COUNT(jd.{ColumnJobClass}) AS Count
              FROM {TablePrefixSubst}{TableTriggers} t
              JOIN {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = '{StateWaiting}' AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))
              GROUP BY jd.{ColumnJobClass} HAVING COUNT(1) > 0";

    public override void Initialize(DelegateInitializationArgs args)
    {
        base.Initialize(args);
        _schedulerName = args.InstanceName;
    }

    public override async Task<IReadOnlyCollection<TriggerAcquireResult>> SelectTriggerToAcquire(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        DateTimeOffset noEarlierThan,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await SelectTriggerToAcquire(conn, noLaterThan, noEarlierThan, maxCount, (null, null), cancellationToken);
    }

    public async Task<IReadOnlyCollection<TriggerAcquireResult>> SelectTriggerToAcquire(ConnectionAndTransactionHolder conn, DateTimeOffset noLaterThan,
        DateTimeOffset noEarlierThan, int maxCount, (IEnumerable<Type> TypesToExclude, IDictionary<Type, int> TypesToLimit) jobTypes,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 1)
        {
            maxCount = 1; // we want at least one trigger back.
        }

        var hasExcludeTypes = jobTypes.TypesToExclude?.Any() ?? false;
        var commandText = new StringBuilder();
        commandText.Append($"SELECT u.{ColumnTriggerName}, u.{ColumnTriggerGroup}, u.{ColumnJobClass} FROM (");
        commandText.Append(hasExcludeTypes ? GetSelectPartExcludingTypes() : GetSelectPartNoExclusions());

        // count to types. Allows fewer UNIONs
        var limitGroups = jobTypes.TypesToLimit
            .GroupBy(a => a.Value)
            .ToDictionary(a => a.Key, a => a.Select(b => b.Key).OrderBy(b => b.FullName).ToArray())
            .OrderBy(a => a.Key).ToArray();

        foreach (var kv in limitGroups)
        {
            commandText.Append("\nUNION SELECT * FROM (\n");
            commandText.Append(GetSelectPartLimitType(kv.Key));
            commandText.Append("\n)");
        }

        commandText.Append($") u\nORDER BY {ColumnPriority} DESC, {ColumnNextFireTime} ASC\nLIMIT @limit");
        await using var cmd = PrepareCommand(conn, ReplaceTablePrefix(commandText.ToString()));
        AddCommandParameter(cmd, "schedulerName", _schedulerName);
        AddCommandParameter(cmd, "state", StateWaiting);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(noEarlierThan));
        AddCommandParameter(cmd, "limit", maxCount);
        if (hasExcludeTypes) cmd.AddArrayParameters("types", GetJobClasses(jobTypes.TypesToExclude));

        foreach (var kv in limitGroups)
        {
            cmd.AddArrayParameters($"limit{kv.Key}Types", GetJobClasses(kv.Value));
            AddCommandParameter(cmd, $"limit{kv.Key}", kv.Key);
        }

        await using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerAcquireResult> nextTriggers = new();
        // signal cancel, otherwise ADO.NET might have trouble handling partial reads from open reader
        var shouldStop = false;
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (shouldStop)
            {
                cmd.Cancel();
                break;
            }

            if (nextTriggers.Count < maxCount)
            {
                var result = new TriggerAcquireResult(
                    (string)rs[ColumnTriggerName],
                    (string)rs[ColumnTriggerGroup],
                    (string)rs[ColumnJobClass]);
                nextTriggers.Add(result);
            }
            else
            {
                shouldStop = true;
            }
        }

        return nextTriggers;
    }

    public virtual async Task<int> SelectWaitingTriggerCount(ConnectionAndTransactionHolder conn, DateTimeOffset noLaterThan, DateTimeOffset noEarlierThan,
        (IEnumerable<Type> TypesToExclude, IDictionary<Type, int> TypesToLimit) jobTypes, CancellationToken cancellationToken = new())
    {
        var hasExcludeTypes = jobTypes.TypesToExclude?.Any() ?? false;
        var commandText = new StringBuilder();
        commandText.Append("SELECT Count(1) FROM (");
        commandText.Append(hasExcludeTypes ? GetSelectPartExcludingTypes() : GetSelectPartNoExclusions());

        // count to types. Allows fewer UNIONs
        var limitGroups = jobTypes.TypesToLimit
            .GroupBy(a => a.Value)
            .ToDictionary(a => a.Key, a => a.Select(b => b.Key).OrderBy(b => b.FullName).ToArray())
            .OrderBy(a => a.Key).ToArray();

        foreach (var kv in limitGroups)
        {
            commandText.Append("\nUNION SELECT * FROM (\n");
            commandText.Append(GetSelectPartLimitType(kv.Key));
            commandText.Append("\n)");
        }

        commandText.Append(") u");
        await using var cmd = PrepareCommand(conn, ReplaceTablePrefix(commandText.ToString()));

        AddCommandParameter(cmd, "schedulerName", _schedulerName);
        AddCommandParameter(cmd, "state", StateWaiting);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(noEarlierThan));
        if (hasExcludeTypes) cmd.AddArrayParameters("types", GetJobClasses(jobTypes.TypesToExclude));

        foreach (var kv in limitGroups)
        {
            cmd.AddArrayParameters($"limit{kv.Key}Types", GetJobClasses(kv.Value));
            AddCommandParameter(cmd, $"limit{kv.Key}", kv.Key);
        }

        var rs = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(rs);
    }

    public virtual async Task<int> SelectBlockedTriggerCount(ConnectionAndTransactionHolder conn, DateTimeOffset noLaterThan, DateTimeOffset noEarlierThan,
        (IEnumerable<Type> TypesToExclude, IDictionary<Type, int> TypesToLimit) jobTypes, CancellationToken cancellationToken = new())
    {
        await using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SelectBlockedTriggerCountSql));
        AddCommandParameter(cmd, "schedulerName", _schedulerName);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(noEarlierThan));
        // add the limited types, then we'll subtract the ones that are allowed to run later
        cmd.AddArrayParameters("types", GetJobClasses(jobTypes.TypesToExclude.Union(jobTypes.TypesToLimit.Keys)));

        var rs = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        // Value is the number of jobs that are NOT blocked, so we subtract them
        return rs - jobTypes.TypesToLimit.Sum(a => a.Value);
    }

    public virtual async Task<int> UpdateTriggerStatesForJobFromOtherState(ConnectionAndTransactionHolder conn, IEnumerable<Type> jobTypesToInclude,
        string state, string oldState, CancellationToken cancellationToken = default)
    {
        await using var cmd = PrepareCommand(conn, ReplaceTablePrefix(UpdateJobTriggerStatesFromOtherStateSql));
        AddCommandParameter(cmd, "schedulerName", _schedulerName);
        cmd.AddArrayParameters("types", GetJobClasses(jobTypesToInclude));
        AddCommandParameter(cmd, "state", state);
        AddCommandParameter(cmd, "oldState", oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Dictionary<Type, int>> SelectJobTypeCounts(ConnectionAndTransactionHolder conn, ITypeLoadHelper loadHelper,
        DateTimeOffset noLaterThan, CancellationToken cancellationToken = new())
    {
        await using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SelectJobClassesAndCountSql));
        AddCommandParameter(cmd, "schedulerName", _schedulerName);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1)));

        var result = new Dictionary<Type, int>();
        await using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        do
        {
            var jobType = loadHelper.LoadType(rs.GetString(ColumnJobClass)!)!;
            result[jobType] = rs.GetInt32("Count")!;
        } while (await rs.ReadAsync(cancellationToken));

        return result;
    }
}
