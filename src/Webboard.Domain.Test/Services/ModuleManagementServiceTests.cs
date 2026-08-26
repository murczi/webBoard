namespace Webboard.Domain.Test.Services;

using Domain.Interfaces.Repositories;
using Domain.Model.Modules;
using Domain.Services;

public class ModuleManagementServiceTests {
    [Fact]
    public async Task AddAsync_AllowsHttpModuleWithoutHost() {
        var repository = new FakeModuleRepository();
        var service = new ModuleManagementService(repository);
        var module = NewModule();

        await service.AddAsync(module, 1, "Test change");

        Assert.Single(repository.Modules);
        Assert.Null(module.HostId);
        Assert.Equal("https://example.test/health", module.HealthCheckUrl);
        Assert.NotEqual(default, module.DateCreated);
    }

    [Fact]
    public async Task AddAsync_AllowsConfiguredHost() {
        var repository = new FakeModuleRepository { ValidHostId = 4 };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.HostId = 4;

        await service.AddAsync(module, 1, "Test change");

        Assert.Equal(4, Assert.Single(repository.Modules).HostId);
    }

    [Fact]
    public async Task AddAsync_RejectsUnknownHost() {
        var service = new ModuleManagementService(new FakeModuleRepository());
        var module = NewModule();
        module.HostId = 99;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));

        Assert.Contains("valid host", exception.Message);
    }

    [Fact]
    public async Task AddAsync_RejectsUnknownType() {
        var repository = new FakeModuleRepository { ValidTypeId = 2 };
        var service = new ModuleManagementService(repository);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(NewModule(), 1, "Test change"));

        Assert.Contains("valid module type", exception.Message);
    }

    [Fact]
    public async Task AddAsync_ConfiguresDockerContainerInsteadOfHealthUrl() {
        var repository = new FakeModuleRepository
        {
            ValidTypeId = 2,
            ValidTypeName = "Docker",
            ValidHostId = 4
        };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 2;
        module.HostId = 4;
        module.ContainerId = "container-id";

        await service.AddAsync(module, 1, "Test change");

        Assert.Equal("container-id", module.ContainerId);
        Assert.Null(module.HealthCheckUrl);
    }

    [Fact]
    public async Task AddAsync_RequiresHostAndContainerForDockerModule() {
        var repository = new FakeModuleRepository { ValidTypeId = 2, ValidTypeName = "Docker" };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 2;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_ConfiguresSystemdServiceInsteadOfHealthUrl() {
        var repository = new FakeModuleRepository
        {
            ValidTypeId = 3,
            ValidTypeName = "Systemd",
            ValidHostId = 4
        };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 3;
        module.HostId = 4;
        module.ServiceName = "nginx.service";

        await service.AddAsync(module, 1, "Test change");

        Assert.Equal("nginx.service", module.ServiceName);
        Assert.Null(module.HealthCheckUrl);
        Assert.Null(module.ContainerId);
    }

    [Fact]
    public async Task AddAsync_RequiresHostAndServiceForSystemdModule() {
        var repository = new FakeModuleRepository { ValidTypeId = 3, ValidTypeName = "Systemd" };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 3;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_RejectsInvalidSystemdServiceName() {
        var repository = new FakeModuleRepository
        {
            ValidTypeId = 3,
            ValidTypeName = "Systemd",
            ValidHostId = 4
        };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 3;
        module.HostId = 4;
        module.ServiceName = "nginx.service --now";

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));

        Assert.Contains("valid systemd service", exception.Message);
    }

    [Fact]
    public async Task AddAsync_ConfiguresMinecraftServerInsteadOfOtherChecks() {
        var repository = new FakeModuleRepository
        {
            ValidTypeId = 4,
            ValidTypeName = "Minecraft"
        };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 4;
        module.HostId = 99;
        module.MinecraftServerAddress = "mc.example.test";
        module.MinecraftServerPort = 25565;

        await service.AddAsync(module, 1, "Test change");

        Assert.Equal("mc.example.test", module.MinecraftServerAddress);
        Assert.Equal(25565, module.MinecraftServerPort);
        Assert.Null(module.HostId);
        Assert.Null(module.HealthCheckUrl);
        Assert.Null(module.ContainerId);
        Assert.Null(module.ServiceName);
    }

    [Fact]
    public async Task AddAsync_AllowsMinecraftModuleWithoutHost() {
        var repository = new FakeModuleRepository { ValidTypeId = 4, ValidTypeName = "Minecraft" };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 4;
        module.MinecraftServerAddress = "mc.example.test";
        module.MinecraftServerPort = 25565;

        await service.AddAsync(module, 1, "Test change");

        Assert.Null(module.HostId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task AddAsync_RejectsInvalidMinecraftPort(int port) {
        var repository = new FakeModuleRepository
        {
            ValidTypeId = 4,
            ValidTypeName = "Minecraft"
        };
        var service = new ModuleManagementService(repository);
        var module = NewModule();
        module.TypeId = 4;
        module.MinecraftServerAddress = "mc.example.test";
        module.MinecraftServerPort = port;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));

        Assert.Contains("port", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ftp://example.test/health")]
    [InlineData("/relative/health")]
    [InlineData("https://user:secret@example.test/health")]
    public async Task AddAsync_RejectsInvalidHealthCheckUrl(string healthCheckUrl) {
        var service = new ModuleManagementService(new FakeModuleRepository());
        var module = NewModule();
        module.HealthCheckUrl = healthCheckUrl;

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(module, 1, "Test change"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_RequiresAuditComment(string auditComment) {
        var service = new ModuleManagementService(new FakeModuleRepository());

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(NewModule(), 1, auditComment));

        Assert.Contains("Audit comment", exception.Message);
    }

    [Fact]
    public async Task AddAsync_RejectsAuditCommentOverDatabaseLimit() {
        var service = new ModuleManagementService(new FakeModuleRepository());

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(NewModule(), 1, new string('a', 101)));

        Assert.Contains("100", exception.Message);
    }

    private static ModuleModel NewModule() => new()
    {
        Name = "Example site",
        Description = "A test module",
        TypeId = 1,
        HealthCheckUrl = "https://example.test/health",
        ManagementUrl = "https://example.test",
        IsEnabled = true
    };

    private sealed class FakeModuleRepository : IModuleRepository {
        public List<ModuleModel> Modules { get; } = [];
        public int ValidTypeId { get; init; } = 1;
        public string ValidTypeName { get; init; } = "Http";
        public int? ValidHostId { get; init; }

        public Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModuleModel>>(Modules);

        public Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModuleOptionModel>>([]);

        public Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModuleOptionModel>>([]);

        public Task<bool> HostExistsAsync(int hostId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ValidHostId == hostId);

        public Task<string?> GetTypeNameAsync(int typeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(typeId == ValidTypeId ? ValidTypeName : null);

        public Task AddAsync(
            ModuleModel module,
            int actorId,
            string auditComment,
            CancellationToken cancellationToken = default) {
            module.Id = Modules.Count + 1;
            Modules.Add(module);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(
            ModuleModel module,
            int actorId,
            string auditComment,
            CancellationToken cancellationToken = default) {
            var index = Modules.FindIndex(item => item.Id == module.Id);
            if (index < 0)
                return Task.FromResult(false);
            Modules[index] = module;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(
            int moduleId,
            int actorId,
            string auditComment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Modules.RemoveAll(module => module.Id == moduleId) > 0);
    }
}
