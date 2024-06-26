﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Shoko.Server.Repositories;
using Shoko.Server.Scheduling.Acquisition.Attributes;
using Shoko.Server.Scheduling.Attributes;
using Shoko.Server.Services;

namespace Shoko.Server.Scheduling.Jobs.Shoko;

[DatabaseRequired]
[JobKeyMember("ScanFolder")]
[JobKeyGroup(JobKeyGroup.Actions)]
internal class ScanFolderJob : BaseJob
{
    private readonly ActionService _actionService;
    private string _importFolder;

    [JobKeyMember]
    public int ImportFolderID { get; set; }
    public override string TypeName => "Scan Import Folder";
    public override string Title => "Scanning Import Folder";
    public override void PostInit()
    {
        _importFolder = RepoFactory.ImportFolder?.GetByID(ImportFolderID)?.ImportFolderName;
    }
    public override Dictionary<string, object> Details => new() { { "Import Folder", _importFolder ?? ImportFolderID.ToString() } };

    public override async Task Process()
    {
        await _actionService.RunImport_ScanFolder(ImportFolderID);
    }

    public ScanFolderJob(ActionService actionService)
    {
        _actionService = actionService;
    }

    protected ScanFolderJob() { }
}
