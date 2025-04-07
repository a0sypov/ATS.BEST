using ATS.BEST.Services;

namespace ATS.BEST
{
    public class CV
    {
        public string name { get; set; }
        public string applied_role { get; set; }
        public Contacts contacts { get; set; }
        public List<Language> languages { get; set; }
        public string summary { get; set; }
        public List<WorkExperience> work_experience { get; set; }
        public List<Education> education { get; set; }
        public List<Skill> skills { get; set; }
        public List<Project> projects { get; set; }
        public List<string> hobbies { get; set; }
    }


    public abstract class CVSection
    {
        public abstract override string ToString();
    }

    public class Language : CVSection
    {
        public string name { get; set; }
        public string level { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"{name} - {level}";
        }
    }

    public class Skill : CVSection
    {
        public string name { get; set; }
        public string level { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"{name} - {level}";
        }
    }

    public class Technology : CVSection
    { 
        public string name { get; set; }

        public override string ToString()
        {
            return name;
        }
    }

    public class Project : CVSection
    {
        public string name { get; set; }
        public string description { get; set; }
        public List<Technology> technologies { get; set; }

        // ToString()
        public override string ToString()
        {
            string allTechnologies = string.Join(", ", technologies.Select(t => t.name));
            return $"Title: {name}:\nUsed technologies: {allTechnologies}\nDescription: {description}\n";
        }
    }

    public class Contacts : CVSection
    {
        public string phone { get; set; }
        public string email { get; set; }
        public List<string> links { get; set; }
        public string location { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Phone: {phone}\nEmail: {email}\nLocation: {location}\nLinks: {string.Join(", ", links)}\n";
        }
    }

    public class WorkExperience : CVSection
    {
        public string role { get; set; }
        public string company { get; set; }
        public string dates { get; set; }
        public List<string> responsibilities { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Role: {role}:\nCompany: {company}\nDates: {dates}\nResponsibilities: {string.Join("; ", responsibilities)}\n";
        }
    }

    public class Education : CVSection
    {
        public string title { get; set; }
        public string institution { get; set; }
        public string dates { get; set; }
        public string type { get; set; }

        // ToString()
        public override string ToString()
        {
            return $"Title: {title}:\nType: {type}\nInstitution: {institution}\nDates: {dates}\n";
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddHttpClient(); // for HttpClientFactory
            builder.Services.AddSingleton<OpenAIService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapControllers();

            

            app.Run();
        }
    }
}
