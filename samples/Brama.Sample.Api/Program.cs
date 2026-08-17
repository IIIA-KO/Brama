WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

app.MapGet("/", () => Brama.Generated.Greeting.Message);

await app.RunAsync();
