﻿namespace KeyAuthorization
{
    using System;
    using System.Text;
    using System.Net.Http;
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Security.Cryptography;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Reflection;

    class ClientAuth
    {
        #region Variables

        /// <summary>
        /// The http client we use to send commands to our server.
        /// </summary>
        protected static readonly HttpClient client = new HttpClient();

        protected bool authorized = false;

        protected string username = null;

        protected string password = null;

        protected int incrementor = 0;

        protected int heartRate = 0;

        protected string dkey = string.Empty;

        protected string ekey = string.Empty;

        protected bool registeredFlag = false;

        protected byte[] gameCheats;

        #endregion

        #region Enums

        public enum LoginState
        {
            Logged_In,
            Logged_In_Without_Time,
            Password_Failure,
            IP_Mismatch,
            User_doesnt_Exist,
            Response_Error,
            Not_logged_In,
            User_Banned
        }

        #endregion

        #region Structs
        public struct CheatItems
        {
            public string shortname;
            public string classname;
            public string cheatname;
            public string description;
        }

        protected struct LoginResponse
        {
            public string dkey;
            public int heartrate;
            public int heartrhythm;
            public string loggedin;
            public UInt64 meatball;
            public string gamesjson;
        }

        protected struct TimeResponse
        {
            public int timeleft;
        }

        protected struct KeyResponse
        {
            public bool keyres;
        }

        protected struct DownloadFileResponse
        {
            public string file;
            public string error;
        }
        protected struct RegisterUserResponse
        {
            public string dkey;
            public bool addres;
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Class constructor
        /// </summary>
        public ClientAuth()
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "tool", "version" }
            };

            string Json = JsonConvert.SerializeObject(values);
            Task<string> getVersion = Task.Run(() => PostURI(new Uri("http://159.223.114.162/update/update.php"), new FormUrlEncodedContent(values)));
            getVersion.Wait();

            if (getVersion.Result.Equals(Assembly.GetExecutingAssembly().GetName().Version.ToString()))
            {
                GetEncryptionKey();
            }

            else
            {
                if (File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}\\Updater.exe"))
                {
                    DevExpress.XtraEditors.XtraMessageBox.Show("A new update is avaliable for the Cheat Loader.", $"Update: v{getVersion.Result}");
                    Process.Start($"{AppDomain.CurrentDomain.BaseDirectory}\\Updater.exe");
                }

                else
                {
                    DevExpress.XtraEditors.XtraMessageBox.Show("A new update is avaliable for the Cheat Loader.\nPlease run 'Updater.exe' to update to the latest version of the Cheat Loader!", $"Update: v{getVersion.Result}");
                }

                Process.GetCurrentProcess().Kill();
            }
        }

        /// <summary>
        /// Overloaded constructor to prevent default behavior on Admin API.
        /// </summary>
        /// <param name="b"></param>
        protected ClientAuth(bool Admin = true)
        {
            if (Admin)
            {
                GetEncryptionKey();
            }
        }

        /// <summary>
        /// When the user first logs in to the server.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <param name="bitcount"></param>
        /// <returns></returns>
        public LoginState Login(string user, string password, string cheat_type = "external", string bitcount = "x64")
        {
            IsSafe();
            if (dkey.Equals(string.Empty) || registeredFlag)
            {
                this.username = user;
                this.password = password;
                LoginState state = GetDecryptionKey(user, password, cheat_type, bitcount);
                
                this.authorized = (state.Equals(LoginState.Logged_In) || state.Equals(LoginState.Logged_In_Without_Time));
                return state;
            }

            this.authorized = false;
            return LoginState.Response_Error;
        }

        /// <summary>
        /// Attempt to redeem a key, return boolean result of attempt.
        /// </summary>
        /// <param name="timeKey"></param>
        /// <returns></returns>
        public bool RedeemKey(string timeKey)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "key", timeKey },
                { "username", this.username }
            };

            if (Authorized)
            {
                string commandResponse = SendCommand(this.username, this.password, "redeem_key", JsonConvert.SerializeObject(values));

                if (!commandResponse.Equals(string.Empty))
                {
                    KeyResponse keyResponse = JsonConvert.DeserializeObject<KeyResponse>(commandResponse);
                    return keyResponse.keyres;
                }
            }

            return false;
        }

        /// <summary>
        /// Get a users time left
        /// </summary>
        /// <returns>The seconds left until auth end date.</returns>
        public int GetTimeLeft()
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "username", this.username }
            };

            if (Authorized)
            {
                string commandResponse = SendCommand(this.username, this.password, "time_check", JsonConvert.SerializeObject(values));

                if (!commandResponse.Equals(string.Empty))
                {
                    TimeResponse timeResponse = JsonConvert.DeserializeObject<TimeResponse>(commandResponse);
                    return timeResponse.timeleft;
                }
            }

            return 0;
        }

        /// <summary>
        /// Attempt to redeem a key, return boolean result of attempt.
        /// </summary>
        /// <param name="timeKey"></param>
        /// <returns></returns>
        public byte[] DownloadCheat(string type, string gameName)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "filetype", type },
                { "game", gameName }
            };

            if (AuthorizedWithTimeLeft)
            {
                string dllResponse = SendCommand(this.username, this.password, "download_file", JsonConvert.SerializeObject(values));

                if (!dllResponse.Equals(string.Empty))
                {
                    DownloadFileResponse FileResponse = JsonConvert.DeserializeObject<DownloadFileResponse>(dllResponse);

                    if (IsBase64String(FileResponse.file))
                    {
                        return Convert.FromBase64String(FileResponse.file);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempt to register a user.
        /// </summary>
        /// <param name="email"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public bool RegisterUser(string email, string username, string password)
        {
            if (dkey.Equals(string.Empty) && !(ekey.Equals(string.Empty) || ekey.Equals("0") || !IsBase64String(ekey)))
            {
                Dictionary<string, string> parms = new Dictionary<string, string>
                {
                    { "email", email },
                    { "username", username },
                    { "password", password }
                };

                Dictionary<string, string> values = new Dictionary<string, string>
                {
                    { "username", string.Empty },
                    { "password", string.Empty },
                    { "cheese", "register_user" },
                    { "noodles", GenerateFileChallenge().ToString("X16")},
                    { "parms", JsonConvert.SerializeObject(parms) }
                };

                string Json = JsonConvert.SerializeObject(values);
                string EncryptedJson = EncryptString(Json);
                Task<string> response = Task.Run(() => PostURI(new Uri("http://159.223.114.162/index.php"), new FormUrlEncodedContent(new Dictionary<string, string> { { "bluecheese", EncryptedJson } })));
                response.Wait();

                if (!response.Result.Equals(string.Empty))
                {
                    byte[] data = Convert.FromBase64String(response.Result);
                    string decodedString = Encoding.UTF8.GetString(data);
                    RegisterUserResponse registerUserResponse = JsonConvert.DeserializeObject<RegisterUserResponse>(decodedString);
                    this.dkey = registerUserResponse.dkey;

                    registeredFlag = true;

                    return registerUserResponse.addres;
                }
            }

            return false;
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Get the Decryption key
        /// </summary>
        protected LoginState GetDecryptionKey(string username, string password, string cheat_type, string bitcount)
        {
            this.authorized = false;
            if ((dkey.Equals(string.Empty) || registeredFlag) && !(ekey.Equals(string.Empty) || ekey.Equals("0") || !IsBase64String(ekey)))
            {
                Dictionary<string, string> values = new Dictionary<string, string>
                {
                    { "username", username },
                    { "password", password },
                    { "cheese", "get_dkey" },
                    { "noodles", GenerateFileChallenge().ToString("X16")},
                    { "parms", JsonConvert.SerializeObject(new Dictionary<string, string> { { "dir", cheat_type }, { "bitcount", bitcount } }) }
                };

                string Json = JsonConvert.SerializeObject(values);
                string EncryptedJson = EncryptString(Json);
                Task<string> response = Task.Run(() => PostURI(new Uri("http://159.223.114.162/index.php"), new FormUrlEncodedContent(new Dictionary<string, string> { { "bluecheese", EncryptedJson } })));
                response.Wait();

                if (!response.Result.Equals(string.Empty))
                {
                    byte[] data = Convert.FromBase64String(response.Result);
                    string decodedString = Encoding.UTF8.GetString(data);

                    LoginResponse dkeyResponse = JsonConvert.DeserializeObject<LoginResponse>(decodedString);

                    if (Enum.TryParse(dkeyResponse.loggedin, out LoginState state))
                    {
                        if (state.Equals(LoginState.Logged_In) || state.Equals(LoginState.Logged_In_Without_Time))
                        {
                            this.heartRate = dkeyResponse.heartrate;
                            this.dkey = dkeyResponse.dkey;
                            this.gameCheats = Convert.FromBase64String(!dkeyResponse.gamesjson.Equals("false") ? dkeyResponse.gamesjson : "");
                            Task.Run(() => Heartbeat());
                            Task.Run(() => Heartrate(dkeyResponse.heartrhythm));
                            this.authorized = true;
                        }

                        return state;
                    }
                }
            }

            return LoginState.Response_Error;
        }

        /// <summary>
        /// Attempt to log in to the server. (Used for a authentication check every 5 seconds)
        /// </summary>
        /// <returns></returns>
        protected LoginState Login(string user, string password)
        {
            IsSafe();
            if (!dkey.Equals(string.Empty))
            {
                this.username = user;
                this.password = password;

                string commandResponse = SendCommand(user, password, "login", string.Empty);

                if (!commandResponse.Equals(string.Empty))
                {
                    LoginResponse loginResponse = JsonConvert.DeserializeObject<LoginResponse>(commandResponse);

                    if (CheckTimeStamp(loginResponse.meatball) &&
                        Enum.TryParse(loginResponse.loggedin, out LoginState myStatus))
                    {
                        this.authorized = (myStatus.Equals(LoginState.Logged_In) || myStatus.Equals(LoginState.Logged_In_Without_Time));
                        return myStatus;
                    }
                }
            }

            this.authorized = false;
            return LoginState.Response_Error;
        }

        /// <summary>
        /// Checks if the user is logged in every 5 seconds.
        /// </summary>
        /// <returns></returns>
        protected Task Heartbeat()
        {
            LoginState State = this.Login(this.Username, this.Password);
            while (State.Equals(LoginState.Logged_In) || State.Equals(LoginState.Logged_In_Without_Time))
            {
                State = this.Login(this.Username, this.Password);
                incrementor = 0;
                Thread.Sleep(5000);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Increments a int to coenside with the heartbeat. Basic NOP crack protection.
        /// </summary>
        /// <param name="heartRhythm">Milliseconds to increment from between heart beats.</param>
        /// <returns></returns>
        protected Task Heartrate(int heartRhythm)
        {
            while (true)
            {
                incrementor++;
                Thread.Sleep(heartRhythm);
            }
            #pragma warning disable CS0162 // Unreachable code detected
            return Task.CompletedTask;
            #pragma warning restore CS0162 // Unreachable code detected
        }

        /// <summary>
        /// Get the file bytes to send to the server and verify the files integrity.
        /// </summary>
        /// <returns></returns>
        private static UInt64 GenerateFileChallenge()
        {
            /*
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead($"{AppDomain.CurrentDomain.BaseDirectory}{AppDomain.CurrentDomain.FriendlyName}"))
                {
                    return BitConverter.ToUInt64(md5.ComputeHash(stream), 0);
                }
            }
            */

            return Convert.ToUInt64(Process.GetCurrentProcess().MainModule.ModuleMemorySize);
        }

        /// <summary>
        /// Get the Encryption Key
        /// </summary>
        protected void GetEncryptionKey()
        {
            if (ekey.Equals(string.Empty))
            {
                var getEncryptionKey = Task.Run(() => PostURI(new Uri("http://159.223.114.162/index.php"), new FormUrlEncodedContent(new Dictionary<string, string> { { "cheese", "90kGPILHd22/yQ3bctAPwxzEPq+BEA4og3Wqh+hSRFQ=" } })));
                getEncryptionKey.Wait();

                byte[] data = Convert.FromBase64String(getEncryptionKey.Result);
                string decodedString = Encoding.UTF8.GetString(data);

                this.ekey = decodedString;
            }
        }

        /// <summary>
        /// Check to make sure we can login twice within the 800000000 nano second window
        /// </summary>
        /// <returns></returns>
        protected bool CheckTimeStamp(UInt64 unixTimeStamp)
        {
            string commandResponse = SendCommand(this.username, this.password, "login", string.Empty);

            if (!commandResponse.Equals(string.Empty))
            {
                LoginResponse loginResponse = JsonConvert.DeserializeObject<LoginResponse>(commandResponse);

                if (Enum.TryParse(loginResponse.loggedin, out LoginState myStatus) &&
                    myStatus.Equals(LoginState.Logged_In) || myStatus.Equals(LoginState.Logged_In_Without_Time))
                    this.authorized = true;

                UInt64 unixTimeStamp2 = loginResponse.meatball;
                if ((unixTimeStamp2 - unixTimeStamp) < 800000000)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Send a command to our server.
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="command"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        protected string SendCommand(string username, string password, string command, string parameters)
        {
            if (!(ekey.Equals(string.Empty) || ekey.Equals("0")) && !(dkey.Equals(string.Empty) || dkey.Equals("0")) && IsBase64String(ekey) && IsBase64String(dkey))
            {
                Dictionary<string, string> values = new Dictionary<string, string>
                {
                    { "username", username },
                    { "password", password },
                    { "cheese", command },
                    { "noodles", GenerateFileChallenge().ToString("X16")},
                    { "parms", parameters }
                };

                string Json = JsonConvert.SerializeObject(values);
                Task<string> runPostRequestTask = Task.Run(() => PostURI(new Uri("http://159.223.114.162/index.php"), new FormUrlEncodedContent(new Dictionary<string, string> { { "bluecheese", EncryptString(Json) } })));
                runPostRequestTask.Wait();

                return !runPostRequestTask.Result.Equals(string.Empty) ? DecryptString(runPostRequestTask.Result) : string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Is this string a Base64 value?
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        protected bool IsBase64String(string s)
        {
            s = s.Trim();
            return (s.Length % 4 == 0) && Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", RegexOptions.None);
        }

        /// <summary>
        /// Sends a post request.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="postContent"></param>
        /// <returns>http response body content</returns>
        protected static async Task<string> PostURI(Uri uri, HttpContent postContent)
        {
            try
            {
                string response = string.Empty;
                using (HttpClient client = new HttpClient())
                {
                    string exeHash = GenerateFileChallenge().ToString("X16");

                    // Random Bullshit
                    client.DefaultRequestHeaders.Add("E357FA3E1796978F", "86585B78DAFE862A");
                    client.DefaultRequestHeaders.Add("57ACFA58FDD45144", "46F05E18E29ECD13");
                    client.DefaultRequestHeaders.Add("57ACFB58FDD452F7", "4C6D7290ACC036BF");
                    client.DefaultRequestHeaders.Add("57ACF858FDD44DDE", "731AD80D65542AE4");
                    client.DefaultRequestHeaders.Add("57ACF958FDD44F91", Convert.ToBase64String(Encoding.UTF8.GetBytes(exeHash)));

                    HttpResponseMessage result = await client.PostAsync(uri, postContent);
                    if (result.IsSuccessStatusCode)
                    {
                        response = await result.Content.ReadAsStringAsync();
                    }

                    else
                    {
                        response = string.Empty;
                    }
                }
                return response;
            }

            catch (HttpRequestException e)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Encrypt a Key
        /// </summary>
        /// <param name="plainText"></param>
        /// <returns></returns>
        protected string EncryptString(string plainText)
        {
            string password = ekey;

            // Create sha256 hash
            SHA256 mySHA256 = SHA256Managed.Create();
            byte[] key = mySHA256.ComputeHash(Encoding.ASCII.GetBytes(password));

            // Create secret IV
            byte[] iv = new byte[16] { 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0x0, 0x0, 0x0, 0xe, 0x0 };

            // Instantiate a new Aes object to perform string symmetric encryption
            Aes encryptor = Aes.Create();
            encryptor.Mode = CipherMode.CBC;
            encryptor.Key = key;
            encryptor.IV = iv;
            encryptor.Padding = PaddingMode.PKCS7;

            MemoryStream memoryStream = new MemoryStream();
            ICryptoTransform aesEncryptor = encryptor.CreateEncryptor();
            CryptoStream cryptoStream = new CryptoStream(memoryStream, aesEncryptor, CryptoStreamMode.Write);

            byte[] plainBytes = Encoding.ASCII.GetBytes(plainText);
            cryptoStream.Write(plainBytes, 0, plainBytes.Length);
            cryptoStream.FlushFinalBlock();

            byte[] cipherBytes = memoryStream.ToArray();
            memoryStream.Close();
            cryptoStream.Close();
            string cipherText = Convert.ToBase64String(cipherBytes, 0, cipherBytes.Length);

            return cipherText;
        }

        /// <summary>
        /// Decrypts a string.
        /// </summary>
        /// <param name="cipherText"></param>
        /// <returns></returns>
        protected string DecryptString(string cipherText)
        {
            string password = dkey;

            // Create sha256 hash
            SHA256 mySHA256 = SHA256Managed.Create();
            byte[] key = mySHA256.ComputeHash(Encoding.ASCII.GetBytes(password));

            // Create secret IV
            byte[] iv = new byte[16] { 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0xf, 0x0, 0x0, 0x0, 0x0, 0xe, 0x0 };

            // Instantiate a new Aes object to perform string symmetric encryption
            Aes encryptor = Aes.Create();
            encryptor.Mode = CipherMode.CBC;
            encryptor.Key = key;
            encryptor.IV = iv;
            encryptor.Padding = PaddingMode.PKCS7;

            MemoryStream memoryStream = new MemoryStream();
            ICryptoTransform aesDecryptor = encryptor.CreateDecryptor();
            CryptoStream cryptoStream = new CryptoStream(memoryStream, aesDecryptor, CryptoStreamMode.Write);
            string plainText = String.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                cryptoStream.Write(cipherBytes, 0, cipherBytes.Length);

                // Complete the decryption process
                cryptoStream.FlushFinalBlock();

                // Convert the decrypted data from a MemoryStream to a byte array
                byte[] plainBytes = memoryStream.ToArray();

                // Convert the decrypted byte array to string
                plainText = Encoding.ASCII.GetString(plainBytes, 0, plainBytes.Length);
            }

            catch (Exception ex) { }

            finally
            {
                // Close both the MemoryStream and the CryptoStream
                memoryStream.Close();
                cryptoStream.Close();
            }

            // Return the decrypted data as a string
            return plainText;
        }

        /// <summary>
        /// Is this a safe process?
        /// </summary>
        /// <param name="procname"></param>
        /// <returns></returns>
        protected bool IsSafeProcess(string procname)
        {
            string proc = procname.ToLower();
            if (proc == "services") return false;
            if (proc == "registry") return false;
            if (proc == "csrss") return false;
            if (proc == "svchost") return false;
            if (proc == "sgrmbroker") return false;
            if (proc == "msmpeng") return false;
            if (proc == "smss") return false;
            if (proc == "system") return false;
            if (proc == "idle") return false;
            if (proc == "dllhost") return false;
            if (proc == "securityhealthservice") return false;
            if (proc == "wininit") return false;
            if (proc == "nissrv") return false;
            if (proc == "memory compression") return false;
            return true;
        }

        /// <summary>
        /// Does anyone have any bad processes open?
        /// </summary>
        /// <param name="procname"></param>
        /// <returns></returns>
        protected bool BadProcesses(string procname)
        {
            string proc = procname.ToLower();

            if (proc.Contains("idaq")) return true;
            if (proc.Contains("idaq64")) return true;
            if (proc.Contains("ida")) return true;
            if (proc.Contains("ida64")) return true;
            if (proc.Contains("wireshark")) return true;
            if (proc.Contains("ghidra")) return true;
            if (proc.Contains("dnspy")) return true;

            return false;
        }

        /// <summary>
        /// Does the user have any bad programms open?
        /// </summary>
        /// <param name="windowname"></param>
        /// <returns></returns>
        protected bool BadWindowNames(string windowname)
        {
            string winname = windowname.ToLower();
            if (winname.Contains("fiddler")) return true;
            if (winname.Contains("wireshark")) return true;
            if (winname.Contains("ida - ") && winname.Contains(".idb")) return true;
            if (winname.Contains("ida - ") && winname.Contains(".i64")) return true;
            if (winname.Contains("ida v")) return true;
            if (winname.Contains("ghidra")) return true;
            if (winname.Contains("dnspy")) return true;
            return false;
        }

        /// <summary>
        /// Is our program instance safe?
        /// </summary>
        /// <returns></returns>
        protected void IsSafe()
        {
            try
            {
                Process[] procList = Process.GetProcesses();
                if (Directory.Exists($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\dnSpy\\") || Directory.Exists($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\dnSpy\\"))
                {
                    Process.GetCurrentProcess().Kill();
                }
                foreach (Process proc in procList)
                {
                    if (IsSafeProcess(proc.ProcessName))
                    {
                        if (BadProcesses(proc.ProcessName) || BadWindowNames(proc.MainWindowTitle))
                        {
                            proc.Kill();
                        }
                    }
                }

            }

            catch (Exception ex)
            {
                Process.GetCurrentProcess().Kill();
            }
        }

        #endregion

        #region Propertys

        /// <summary>
        /// Are we authorized with time left?
        /// </summary>
        public bool AuthorizedWithTimeLeft
        {
            get { return (Authorized && HasTimeLeft); }
        }

        /// <summary>
        /// Is the client currently authorized?
        /// This is fequently updated by the heartbeat.
        /// </summary>
        public bool Authorized
        {
            get { return authorized && HeartRate; }
        }

        /// <summary>
        /// Get the game cheat json.
        /// </summary>
        public List<CheatItems> GameCheats
        {
            get { return JsonConvert.DeserializeObject<List<CheatItems>>(Encoding.UTF8.GetString(gameCheats)); }
        }


        /// <summary>
        /// Returns the current Datetime for the users time zone.
        /// </summary>
        public DateTime TimeLeft
        {
            get
            {
                return DateTime.Now.AddSeconds(GetTimeLeft());
            }
        }

        /// <summary>
        /// Gets the Years this person has authed left.
        /// </summary>
        public int YearsLeft
        {
            get
            {
                return GetTimeLeft() / 31556952;
            }
        }

        /// <summary>
        /// Gets the months this person has authed left.
        /// </summary>
        public int MonthsLeft
        {
            get
            {
                return (GetTimeLeft() % 31556952) / 2592000;
            }
        }

        /// <summary>
        /// Gets the Days this person has authed left.
        /// </summary>
        public int DaysLeft
        {
            get
            {
                return (GetTimeLeft() % 2592000) / 86400;
            }
        }

        /// <summary>
        /// Gets the Hours this person has authed left.
        /// </summary>
        public int HoursLeft
        {
            get
            {
                return (GetTimeLeft() % 86400) / 3600;
            }
        }

        /// <summary>
        /// Gets the Minutes this person has authed left.
        /// </summary>
        public int MinutesLeft
        {
            get
            {
                return (GetTimeLeft() % 3600) / 60;
            }
        }

        /// <summary>
        /// Gets the seconds this person has authed left.
        /// </summary>
        public int SecondsLeft
        {
            get
            {
                return GetTimeLeft() % 60;
            }
        }

        /// <summary>
        /// Is there time left on this users account?
        /// </summary>
        public bool HasTimeLeft
        {
            get
            {
                return !GetTimeLeft().Equals(0);
            }
        }

        /// <summary>
        /// Veryifys the integrity of the heart beat. 
        /// If the incrementor of the heartrate ever exceeds the heartrate,
        /// that means the first connection thread loop for authorization was nop'ed in memory and someone was poking around where they shouldn't be.
        /// </summary>
        public bool HeartRate
        {
            get { return this.incrementor <= this.heartRate; }
        }

        /// <summary>
        /// Get username that is currently logged in.
        /// </summary>
        public string Username
        {
            get
            {
                if (this.Authorized)
                    return username;
                else
                    return null;
            }
        }

        /// <summary>
        /// Get password that is currently logged in.
        /// </summary>
        public string Password
        {
            get
            {
                if (this.Authorized)
                    return password;
                else
                    return null;
            }
        }

        #endregion
    }
}