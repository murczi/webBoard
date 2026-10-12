namespace Webboard.Infrastructure.Repositories;
using System.Linq.Expressions;
using Configuration.Entities;
public static class ModuleQueries {
    public static readonly Expression<Func<ModuleEntity, bool>> Monitorable = x => x.IsEnabled && !x.DeletionFlag &&
        (x.HostId == null || x.Host != null && x.Host.IsEnabled && !x.Host.DeletionFlag);
    public static readonly Func<ModuleEntity, bool> IsMonitorable = Monitorable.Compile();
}
