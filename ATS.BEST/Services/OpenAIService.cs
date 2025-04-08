using System.Text;
using System.Text.Json;

namespace ATS.BEST.Services
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenAIService(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = config["OpenAI:ApiKey"]; // Store your key in appsettings.json or secrets
        }

        public async Task<string> AI_CVParsing_Async(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { 
                        role = "system", 
                        content =
                        """
                        You are an AI assistant designed to extract structured information from resumes or CVs. Your task is to read the provided resume text and output the data in the following JSON format. If certain fields are missing or cannot be found, use null or an empty array as appropriate. Parse the CV text exactly as provided, without modifications, summaries, or opinions. DO NOT answer or write anything, but json output.              

                        Please return the data in the following structure:
                        {
                          "name": "Full name of the candidate",
                          "applied_role": "Role or job title the person is applying for",
                          "contacts": {
                            "phone": "Phone number",
                            "email": "Email address",
                            "links": ["LinkedIn, GitHub, portfolio, etc."],
                            "location": "City, region, or address if specified"
                          },
                          "languages": [
                            {
                                "name" : "Language name",
                                "level" : "Proficiency level (e.g., fluent, intermediate, basic, C1, B2, A1, not specified etc.)"
                            }],
                          "summary": "Brief personal summary or career overview",
                          "work_experience": [
                            {
                              "role": "Job title",
                              "company": "Employer name",
                              "dates": "Start - End or time period",
                              "responsibilities": ["List of duties and accomplishments"]
                            }
                          ],
                          "education": [
                            {
                              "title": "Degree or certificate name",
                              "institution": "School or organization name",
                              "dates": "Start - End or year",
                              "type": "diploma or certification"
                            }
                          ],
                          "skills": [
                           {
                              "name" : "Skill name",
                              "level" : "Proficiency level (e.g., expert, intermediate, basic, not specified, etc.)"
                           }
                          ],
                          "projects": [
                            {
                              "name": "Project name",
                              "description": "Short explanation of the project",
                              "technologies": [
                                {
                                    "name" : "Technology name"
                                }
                              ]
                            }
                          ],
                          "hobbies": ["Personal interests or activities"]
                        }
                        """
                    },
                    
                    new { 
                        role = "user", 
                        content = inputText 
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        public async Task<string> AI_CVEvaluation_Async(string category, string cvSections, string jobDescription)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            string intro = """
                You are an expert Recruitment Evaluation Assistant designed to rigorously assess alignment between **multiple candidates’ qualifications** (from specified CV sections) and a full job description. Your analysis must **compare candidates both against the job requirements and relative to each other**, ensuring fair, systematic, and evidence-based rankings.  

                **Process:**  
                1. **Individual Candidate Analysis:**  
                   - For **each candidate**, follow the original evaluation framework:  
                     - **Cross-Reference:** Compare the CV section against the job description’s *key requirements*, *preferred qualifications*, and *bonus skills*. Prioritize "must-have" criteria.  
                     - **Evaluate Across Dimensions:**  
                        - **Relevance:** Does the CV explicitly address the job’s core competencies? Highlight keyword matches, skill overlap, and industry-specific terminology.  
                        - **Depth:** Assess experience duration, complexity of roles/responsibilities, and quantifiable achievements (e.g., "managed 10+ engineers" vs. "led teams").  
                        - **Completeness:** Identify gaps in required certifications, tools, or experience. Note if absent details could be inferred (e.g., "Python" implying scripting skills).  
                        - **Contextual Awareness:** Does the CV emphasize transferable skills for career shifts? Adjust scoring if the role values potential over direct experience.  
                   - Assign a **draft score (1-10)** using the same criteria.  

                
                2. **Comparative Analysis:**  
                   - **Rank candidates** based on their draft scores and the *differentiators* below:  
                     - **Key Requirement Coverage:** Does one candidate cover more "must-have" criteria or demonstrate greater depth?  
                     - **Unique Value-Add:** Does a candidate offer rare/preferred skills that others lack (e.g., niche certifications, leadership experience)?  
                     - **Risk Mitigation:** Are there fewer gaps/ambiguities in their qualifications compared to peers?  
                
                3. **Avoid Assumptions:** Only consider explicitly stated information. Flag ambiguities (e.g., "proficient in data analysis" without tools/context).  

                **Response Format:**
                - **General formatin:**
                  - **Section Separation:** Separate each candidate evaluation section with '-----\n\n'. **DO NOT** use it before first candidate. Separate with it candidates between each other.
                  - **FINAL RATING Separation:** Using '-----\n\n' separate FINAL RATING from anything before it with it, including section Comparative Analysis.
                - **Candidate Summaries:**  
                  - **{{Candidate Name}}:**  
                    **Response Format:**
                    - **Overall evaluation, your opinion:** [5-10 sentences of overall evaluation.]
                    - **Key Requirements Matched:** [Bullet points linking CV excerpts to job criteria. Be specific: "CV mentions 'AWS cloud architecture' ↔ JD lists 'AWS expertise (required)'."]  
                    - **Strengths:** [Highlight standout qualifications exceeding expectations.]  
                    - **Gaps/Uncertainties:** [Note missing requirements or vague claims needing clarification.]  
                    - **Reasoning Summary:** [2-3 sentences synthesizing alignment level. Example: "CV demonstrates 4/5 core skills with strong depth in backend development but lacks formal certifications listed as preferred."]  
                - **Comparative Analysis:**  
                  - [Highlight key differentiators between candidates. Example: "Candidate A demonstrates AWS expertise (required), while Candidate B lacks this but offers Kubernetes (bonus)."]  
                  - [Note trade-offs: "Candidate C has the most experience but lacks the preferred Scrum certification shared by others."]  

                **Final Rating Guidance:**  
                - Scores must reflect **both individual alignment and relative performance**.

                **Deliverable:**  
                After all your reasonings, on the **final line only**, write:  
                FINAL RATING:  
                {{Candidate Name}} - X  
                {{Candidate Name}} - Y  
                ...  
                [Order candidates from highest to lowest score. No text after ratings.]  
                """;

            string main = "";
            switch (category)
            {
                case "work_experience":
                    main = """  
                        **Evaluation Framework for Professional History**  
                        Assess alignment through these lenses:  

                        1. **Role Alignment Matrix**  
                           - Compare CV job titles/industries to JD's required experience  
                           - Map seniority levels (IC vs leadership roles) using standardized tiers:  
                             Junior (0-3y) | Mid (4-6y) | Senior (7-10y) | Executive (10y+)  

                        2. **Tenure & Progression Analysis**  
                           - Minimum duration thresholds: Check if cumulative experience meets JD's "X+ years" requirements  
                           - Career trajectory: Progressive promotions vs lateral moves  
                           - Gap analysis: Unexplained employment gaps >6 months  

                        3. **Scope of Impact**  
                           - Team/Project Scale: Individual contributor vs managing budgets/teams  
                           - Quantifiable outcomes: Revenue impact, cost savings, efficiency gains  
                           - Specialized achievements: Patents, industry awards, published case studies  

                        4. **Contextual Factors**  
                           - Transferable skills for career changers (e.g., PM skills from consulting → tech)  
                           - Startup vs enterprise experience relevance  

                        **Evaluation Guidance:**  
                        - Prioritize JD's "required experience" over "nice-to-have"  
                        - -1 rating if missing mandatory experience tiers  
                        - +1 rating for exceeding quantitative benchmarks by >20%  
                        """;
                    break;

                case "projects":
                    main = """  
                        **Project Relevance Assessment Protocol**  

                        **Technical Alignment:**  
                        - Match project domains to JD's target areas (e.g., "ML pipeline optimization" ↔ "building data infrastructure")  
                        - Tools/Stack Verification: GitHub links or specific version mentions (TensorFlow 2.x vs generic "AI frameworks")  

                        **Complexity Grading:**  
                        Tier 1: Proof-of-concept/POCs | Tier 2: Department-level implementations | Tier 3: Cross-functional/organizational deployments  

                        **Ownership Spectrum:**  
                        - Solo projects: Full-stack implementation  
                        - Team projects: Differentiate "contributed to" vs "led architecture for"  
                        - Open-source: Merge requests accepted vs personal forks  

                        **Business Impact Validation:**  
                        - User metrics: "Improved API response time by 40%" > "optimized performance"  
                        - Production vs experimental: Shipped features > hackathon projects  

                        **Evaluation Rules:**  
                        - Count projects as experience substitutes ONLY if JD allows equivalent demonstrations  
                        - Disregard school projects for senior roles unless exceptionally relevant  
                        """;
                    break;

                case "education":
                    main = """  
                        **Credential Matching System**  

                        **Core Requirements Check:**  
                        - Degree Type: PhD/Master's/Bachelor's equivalency  
                        - Field Strictness: "Computer Science required" vs "STEM preferred"  
                        - Accreditation: Region-specific validations (ABET, AACSB, etc.)  

                        **Supplementary Certifications:**  
                        - Tier 1: Vendor certifications (AWS Solutions Architect)  
                        - Tier 2: Platform certifications (Google Analytics IQ)  
                        - Tier 3: MOOC certificates (Coursera specializations)  

                        **Institutional Weighting:**  
                        - Only consider school prestige if JD explicitly mentions "Top X universities"  
                        - Adjust for non-traditional backgrounds: Bootcamp grads with 3+ shipped projects  

                        **Evaluation Protocol:**  
                        - Automatic disqualification only if missing absolute requirements (e.g., JD's "MD degree mandatory")  
                        - Treat certifications as 25% experience equivalents unless stated otherwise  
                        """;
                    break;

                case "skills":
                    main = """  
                        **Competency Validation Matrix**  

                        **Skill Hierarchy:**  
                        - Core: JD's "expert in Python" → CV shows 5y professional usage  
                        - Preferred: JD's "familiarity with Rust" → CV lists 6mo side projects  
                        - Bonus: JD's "nice to have Tableau" → CV mentions basic course  

                        **Depth Indicators:**  
                        - Surface-level: Skill listed without context  
                        - Intermediate: Version control (Git), CI/CD exposure  
                        - Expert: Custom plugin development, conference talks  

                        **Proficiency Verification:**  
                        - Claim Type | Supporting Evidence  
                          "Advanced" → Sharded libraries/APIs | "Intermediate" → Bug fixes in existing codebase  

                        **Anti-Inflation Measures:**  
                        - Demote buzzword-heavy lists without substantiation ("AI/Blockchain")  
                        - Flag potential overstatements: "Photoshop (Expert)" without portfolio  
                        """;
                    break;

                case "languages":
                    main = """  
                        **Linguistic Requirements Analysis**  

                        **Proficiency Scale Mapping:**  
                        - A1/A2: Basic communication | B1/B2: Professional fluency | C1/C2: Native-level  
                        - Match to JD needs: "French (B2+ required)" vs "Spanish (nice to have)"  

                        **Certification Validation:**  
                        - Standardized tests: TOEFL (expiry dates), DELE, JLPT levels  
                        - Work Evidence: "Negotiated contracts in Mandarin" > "conversational Chinese"  

                        **Dialectical Requirements:**  
                        - Regional specificity: Latin American vs European Spanish  
                        - Technical jargon: Medical German vs conversational fluency  

                        **Contextual Bonuses:**  
                        +1 rating if surpassing requirements for client-facing global roles  
                        - Never penalize for additional languages beyond requirements  
                        """;
                    break;
            }

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new {
                        role = "system",
                        content = $"{intro}\n{main}"
                    },

                    new {
                        role = "user",
                        content = $"Job Description:\n{jobDescription}\n\n**{category}**:\n\n" + cvSections
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        public async Task<List<float>> GetEmbeddingAsync(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                input = inputText,
                model = "text-embedding-3-small"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error (Embeddings): {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var embeddingArray = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToList();

            return embeddingArray;
        }

        public double CosineSimilarity(List<float> v1, List<float> v2)
        {
            if (v1.Count != v2.Count) throw new ArgumentException("Vector lengths must match");

            double dot = 0.0, norm1 = 0.0, norm2 = 0.0;
            for (int i = 0; i < v1.Count; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += Math.Pow(v1[i], 2);
                norm2 += Math.Pow(v2[i], 2);
            }
            return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

    }
}
