using ATS.BEST.Services;

using Microsoft.AspNetCore.Mvc;

using UglyToad.PdfPig;

using System.Text.Json;
using static System.Collections.Specialized.BitVector32;

namespace ATS.BEST.Controllers
{
    public struct ApplicantScore
    {
        public float finalScore { get; set; }
        public float maxScore { get; set; }
        public float workExperienceScore { get; set; }
        public float projectsScore { get; set; }
        public float educationScore { get; set; }
        public float skillsScore { get; set; }
        public float languagesScore { get; set; }

        public ApplicantScore(float _finalScore, float _maxScore, float _workExperienceScore, float _projectsScore, float _educationScore, float _skillsScore, float _languagesScore)
        {
            finalScore = _finalScore;
            maxScore = _maxScore;
            workExperienceScore = _workExperienceScore;
            projectsScore = _projectsScore;
            educationScore = _educationScore;
            skillsScore = _skillsScore;
            languagesScore = _languagesScore;
        }
    }
    public struct Applicant
    {
        public CV CV { get; set; }
        public string AIEvaluation { get; set; }
        public ApplicantScore Scores { get; set; }

        public Applicant(CV _CV)
        {
            CV = _CV;
            AIEvaluation = "";
            Scores = new ApplicantScore();
        }

        public Applicant(CV _CV, ApplicantScore _Score, string _AIEvaluation)
        {
            CV = _CV;
            Scores = new ApplicantScore(_Score.finalScore, _Score.maxScore, _Score.workExperienceScore, _Score.projectsScore, _Score.educationScore, _Score.skillsScore, _Score.languagesScore);
            AIEvaluation = _AIEvaluation;
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RoutingController : Controller
    {
        private readonly OpenAIService _openAi;

        public RoutingController(OpenAIService openAi)
        {
            _openAi = openAi;
        }

        [HttpPost]
        [Route("upload")]
        public async Task<IActionResult> ConvertPDFs([FromForm] List<IFormFile> cvs, [FromForm] string jobDescription)
        {
            if (cvs == null || cvs.Count == 0)
                return BadRequest("No files uploaded.");

            List<Applicant> applicants = new List<Applicant>();

            List<float> jbEmbedding = await _openAi.GetEmbeddingAsync(jobDescription);

            foreach (var cv in cvs)
            {
                if (cv.Length > 0)
                {
                    using var stream = cv.OpenReadStream();
                    using var pdf = PdfDocument.Open(stream);

                    var fullText = string.Join("\n", pdf.GetPages().Select(p => p.Text));

                    List<float> cvEmbedding = await _openAi.GetEmbeddingAsync(fullText);

                    double cosineSimilarity = 1.0; // _openAi.CosineSimilarity(jbEmbedding, cvEmbedding);

                    if(cosineSimilarity >= 0.5)
                    {
                        string JSONCV = await _openAi.AI_CVParsing_Async(fullText);
                        string cleanedJSONCV = System.Text.RegularExpressions.Regex.Unescape(JSONCV);

                        CV CV = JsonSerializer.Deserialize<CV>(cleanedJSONCV);
                        applicants.Add(new Applicant(CV));
                    }
                }
            }

            for (int i = 0; i < applicants.Count; i++)
            {
                CV cv = applicants[i].CV;
                ApplicantScore newScores = new ApplicantScore();

                List<CVSection> workExperienceSections = cv.work_experience.Cast<CVSection>().ToList();
                string workExperienceEval = await _openAi.AI_CVEvaluation_Async("work_experience", workExperienceSections, jobDescription);
                
                float workExperienceScore = ParseEvaluationScore(workExperienceEval);
                newScores.workExperienceScore = workExperienceScore;

                List<CVSection> projectsSections = cv.projects.Cast<CVSection>().ToList();
                string projectsEval = await _openAi.AI_CVEvaluation_Async("projects", projectsSections, jobDescription);
                float projectsScore = ParseEvaluationScore(projectsEval);
                newScores.projectsScore = projectsScore;

                List<CVSection> educationSections = cv.education.Cast<CVSection>().ToList();
                string educationEval = await _openAi.AI_CVEvaluation_Async("education", educationSections, jobDescription);
                float educationScore = ParseEvaluationScore(educationEval);
                newScores.educationScore = educationScore;

                List<CVSection> skillsSections = cv.skills.Cast<CVSection>().ToList();
                string skillsEval = await _openAi.AI_CVEvaluation_Async("skills", skillsSections, jobDescription);
                float skillsScore = ParseEvaluationScore(skillsEval);
                newScores.skillsScore = skillsScore;

                List<CVSection> languagesSections = cv.languages.Cast<CVSection>().ToList();
                string languagesEval = await _openAi.AI_CVEvaluation_Async("languages", languagesSections, jobDescription);
                float languagesScore = ParseEvaluationScore(languagesEval);
                newScores.languagesScore = languagesScore;

                // Dictionary of section weights
                Dictionary<string, float> sectionWeights = new Dictionary<string, float>
                {
                    { "work_experience", 1.0f },
                    { "projects", 0.7f },
                    { "education", 0.5f },
                    { "skills", 0.3f },
                    { "languages", 0.1f }
                };

                float finalScore = workExperienceScore * sectionWeights["work_experience"] +
                                   projectsScore * sectionWeights["projects"] +
                                   educationScore * sectionWeights["education"] +
                                   skillsScore * sectionWeights["skills"] +
                                   languagesScore * sectionWeights["languages"];
                newScores.finalScore = finalScore;

                float maxScore = 5 * sectionWeights["work_experience"] +
                                   5 * sectionWeights["projects"] +
                                   5 * sectionWeights["education"] +
                                   5 * sectionWeights["skills"] +
                                   5 * sectionWeights["languages"];
                newScores.maxScore = maxScore;

                // float finalScore = workExperienceScore * 0.35f + projectsScore * 0.25f + educationScore * 0.15f + skillsScore * 0.15f + languagesScore * 0.1f;

                // Print of all scores with a name of a person
                Console.WriteLine($"Name: {cv.name}, Work Experience Score: {workExperienceScore}, Projects Score: {projectsScore}, Education Score: {educationScore}, Skills Score: {skillsScore}, Languages Score: {languagesScore}, Final Score: {finalScore}");

                string AIEvaluation = $"Work experience:\n{workExperienceEval}\n\nProjects:\n{projectsEval}\n\nEducation:\n{educationEval}\n\nSkills:\n{skillsEval}\n\nLanguages:\n{languagesEval}";
                applicants[i] = new Applicant(cv, newScores, AIEvaluation);
            }

            await Task.CompletedTask;
            return Ok( applicants );
        }

        private float ParseEvaluationScore(string evaluation)
        {
            float score = 0;
            if (float.TryParse(evaluation.Last().ToString(), out score))
            {
                // Successfully parsed last element
            }
            else if (float.TryParse(evaluation[evaluation.Length - 2].ToString(), out score))
            {
                // Successfully parsed second to last element
            }
            return score;
        }
    }
}
