using EssentialCSharp.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSingleton<IList<SiteMapping>>(sp => {
    IWebHostEnvironment _HostingEnvironment = sp.GetRequiredService<IWebHostEnvironment>();
    string path = Path.Combine(_HostingEnvironment.ContentRootPath,"Chapters","sitemap.json");
    List<SiteMapping>? siteMappings = System.Text.Json.JsonSerializer.Deserialize<List<SiteMapping>>(File.OpenRead(path));
    if (siteMappings is null)
    {
        throw new InvalidOperationException("No table of contents found");
    }
    return siteMappings;
});

var app = builder.Build();
_= app.Services.GetRequiredService<IList<SiteMapping>>();
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(endpoint =>
{
    endpoint.MapDefaultControllerRoute();
    endpoint.MapControllerRoute(
        name: "slug",
        pattern: "{*key}",
        defaults: new { controller = "Home", action = "Index" });
});


app.Run();
