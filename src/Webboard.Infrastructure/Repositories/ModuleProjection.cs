namespace Webboard.Infrastructure.Repositories;
using System.Linq.Expressions;
using Configuration.Entities;
using Domain.Model.Modules;

public static class ModuleProjection {
    public static readonly Expression<Func<ModuleEntity, ModuleModel>> Read = module => new ModuleModel
            {
                Id = module.Id,
                TypeId = module.TypeId,
                TypeName = module.Type != null ? module.Type.Name : string.Empty,
                HostId = module.HostId,
                HostExists = module.Host != null, HostIsEnabled = module.Host != null && module.Host.IsEnabled,
                HostIsDeleted = module.Host != null && module.Host.DeletionFlag,
                HostName = module.Host != null ? module.Host.Name : null,
                HostAgentBaseUrl = module.Host != null ? module.Host.AgentBaseUrl : null,
                Name = module.FriendlyName,
                Description = module.Description,
                HealthCheckUrl = module.HealthCheckUrl,
                ContainerId = module.ContainerId,
                ServiceName = module.ServiceName,
                MinecraftServerAddress = module.MinecraftServerAddress,
                MinecraftServerPort = module.MinecraftServerPort,
                SteamServerAddress = module.SteamServerAddress,
                SteamQueryPort = module.SteamQueryPort,
                SteamQueryPlayers = module.SteamQueryPlayers,
                ManagementUrl = module.ManagementUrl,
                IsEnabled = module.IsEnabled,
                DateCreated = module.DateCreated,
                DateUpdated = module.DateUpdated
            };
    private static readonly Func<ModuleEntity, ModuleModel> map = Read.Compile();
    public static ModuleModel ToModel(ModuleEntity entity) => map(entity);
    public static ModuleEntity ToEntity(ModuleModel module) {
        return new ModuleEntity
        {
            TypeId = module.TypeId,
            HostId = module.HostId,
            FriendlyName = module.Name,
            Description = module.Description,
            HealthCheckUrl = module.HealthCheckUrl,
            ContainerId = module.ContainerId,
            ServiceName = module.ServiceName,
            MinecraftServerAddress = module.MinecraftServerAddress,
            MinecraftServerPort = module.MinecraftServerPort,
            SteamServerAddress = module.SteamServerAddress,
            SteamQueryPort = module.SteamQueryPort,
            SteamQueryPlayers = module.SteamQueryPlayers,
            ManagementUrl = module.ManagementUrl,
            DeletionFlag = false,
            IsEnabled = module.IsEnabled,
            DateCreated = module.DateCreated,
            DateUpdated = module.DateUpdated
        };
    }
}
