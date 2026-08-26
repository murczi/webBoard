var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
   {
       Status = "Healthy",
       Service = "WebBoard.Agent"
   }))
   .WithName("GetHealth")
   .WithTags("Health");

app.Run();
