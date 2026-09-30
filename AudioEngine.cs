using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualAudioMirror
{
    public sealed class TargetSpec
    {
        public string DeviceId;
        public string Name;
        public int DelayMs;
    }

    public sealed class DeviceStats
    {
        public string Name;
        public double BufferMs;
        public double TargetBufferMs;
        public long Underruns;
        public long Overflows;
        public long DriftCorrections;
        public bool Alive;
        public string LastError;
        public long BytesPlayed;
    }

    public sealed class EngineSnapshot
    {
        public bool Running;
        public string MixFormat;
        public double CapturedKbPerSec;
        public bool HasCapturedAudio;
        public string SourceName;
        public IReadOnlyList<DeviceStats> Targets;
        public string LastError;
    }

    public sealed class AudioEngineOptions
    {
        public string SourceDeviceId;
        public List<TargetSpec> Targets;
        public int PrebufferMs = 60;
        public int CaptureBufferMs = 10;
        public int OutputLatencyMs = 20;
        public bool SetDefaultToSource = false;
    }

    public sealed class AudioEngine : IDisposable
    {
        private sealed class LowLatencyLoopback : WasapiCapture
        {
            public LowLatencyLoopback(MMDevice device, int bufferMs)
                : base(device, true, bufferMs)
            {
            }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
            {
                return AudioClientStreamFlags.Loopback | AudioClientStreamFlags.AutoConvertPcm |
                       AudioClientStreamFlags.SrcDefaultQuality;
            }
        }

        private sealed class TargetOutput
        {
            public string DeviceId;
            public string Name;
            public TargetRing Ring;
            public WasapiOut Output;
            public MMDevice Device;
            public DeviceStats Stats;
            public int Restarts;
            public long LastDriftLogTicks;
        }

        private const int MaxDelayMs = 400;

        private readonly object _sync = new object();
        private readonly object _rateLock = new object();
        private readonly List<TargetOutput> _targetList = new List<TargetOutput>();
        private volatile TargetOutput[] _published = new TargetOutput[0];

        private MMDeviceEnumerator _enumerator;
        private MMDevice _sourceDevice;
        private WasapiCapture _capture;
        private Thread _teardownThread;
        private Thread _maintenanceThread;
        private volatile int _maintenanceSession;
        private volatile bool _maintenanceRunning;
        private volatile bool _running;
        private volatile bool _disposed;
        private volatile bool _hasCapturedAudio;
        private volatile int _prebufferMs = 60;
        private volatile int _outputLatencyMs = 20;
        private volatile string _sourceName = "";
        private volatile string _mixFormat = "";
        private volatile string _lastError;

        private long _capturedBytes;
        private long _emptyPackets;
        private long _rateTicks;
        private long _rateBytes;
        private double _rateKbPerSec;

        public event Action<string> ErrorOccurred;

        public AudioEngine()
        {
        }

        public void Start(AudioEngineOptions options)
        {
            if (_disposed)
                throw new InvalidOperationException("O motor de áudio foi encerrado e não pode ser reutilizado.");
            if (options == null)
                throw new InvalidOperationException("As opções do motor não foram informadas.");
            if (string.IsNullOrWhiteSpace(options.SourceDeviceId))
                throw new InvalidOperationException("Selecione o dispositivo principal (fonte do áudio).");
            if (options.Targets == null || options.Targets.Count == 0)
                throw new InvalidOperationException("Marque pelo menos um dispositivo para tocar junto.");

            WaitForPendingTeardown();

            int prebufferMs = Clamp(options.PrebufferMs, 10, 500);
            int captureMs = Clamp(options.CaptureBufferMs, 5, 500);
            int latencyMs = Clamp(options.OutputLatencyMs, 5, 500);

            if (options.SetDefaultToSource)
                TrySetDefault(options.SourceDeviceId);

            bool finished = false;
            try
            {
                lock (_sync)
                {
                    if (_disposed)
                        throw new InvalidOperationException("O motor de áudio foi encerrado e não pode ser reutilizado.");
                    if (_running)
                        throw new InvalidOperationException("O espelhamento já está em execução. Pare antes de iniciar novamente.");

                    Setup(options, prebufferMs, captureMs, latencyMs);
                }

                WaitForPrebuffer(prebufferMs);

                lock (_sync)
                {
                    if (!_running)
                        return;
                    PlayAll();
                    finished = true;
                }
            }
            finally
            {
                if (!finished)
                    StopInternal();
            }
        }

        public void Stop()
        {
            StopInternal();
        }

        public EngineSnapshot GetSnapshot()
        {
            EngineSnapshot snapshot = new EngineSnapshot();
            snapshot.Running = _running;
            snapshot.MixFormat = _mixFormat;
            snapshot.SourceName = _sourceName;
            snapshot.LastError = _lastError;
            snapshot.HasCapturedAudio = _hasCapturedAudio;
            snapshot.CapturedKbPerSec = UpdateRate();

            TargetOutput[] targets = _published;
            List<DeviceStats> list = new List<DeviceStats>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
                list.Add(CopyStats(targets[i]));
            snapshot.Targets = list;
            return snapshot;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            StopInternal();

            lock (_sync)
            {
                if (_enumerator != null)
                {
                    DisposeQuietly(_enumerator);
                    _enumerator = null;
                }
            }
        }

        private void Setup(AudioEngineOptions options, int prebufferMs, int captureMs, int latencyMs)
        {
            _prebufferMs = prebufferMs;
            _outputLatencyMs = latencyMs;
            _lastError = null;
            _hasCapturedAudio = false;
            Interlocked.Exchange(ref _capturedBytes, 0);
            Interlocked.Exchange(ref _emptyPackets, 0);
            lock (_rateLock)
            {
                _rateTicks = DateTime.UtcNow.Ticks;
                _rateBytes = 0;
                _rateKbPerSec = 0;
            }

            if (_enumerator == null)
                _enumerator = new MMDeviceEnumerator();

            MMDevice source = null;
            try
            {
                source = _enumerator.GetDevice(options.SourceDeviceId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Não foi possível abrir o dispositivo de origem: " + ex.Message);
            }
            if (source == null)
                throw new InvalidOperationException("Não foi possível abrir o dispositivo de origem: dispositivo não encontrado.");
            if ((source.State & DeviceState.Active) == 0)
            {
                DisposeQuietly(source);
                throw new InvalidOperationException("O dispositivo de origem não está ativo. Verifique se ele está conectado e habilitado.");
            }

            _sourceDevice = source;
            _sourceName = SafeName(source);

            try
            {
                _capture = new LowLatencyLoopback(source, captureMs);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Não foi possível abrir a captura de \"" + _sourceName + "\": " + ex.Message);
            }

            WaveFormat format = _capture.WaveFormat;
            if (format == null)
                throw new InvalidOperationException("Não foi possível obter o formato de áudio do dispositivo de origem.");
            _mixFormat = format.ToString();

            List<string> failures = new List<string>();
            for (int i = 0; i < options.Targets.Count; i++)
            {
                TargetSpec spec = options.Targets[i];
                if (spec == null)
                    continue;
                if (string.IsNullOrWhiteSpace(spec.DeviceId))
                {
                    failures.Add("dispositivo sem identificação");
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(spec.Name) ? spec.DeviceId : spec.Name;
                int delayMs = spec.DelayMs < 0 ? 0 : (spec.DelayMs > MaxDelayMs ? MaxDelayMs : spec.DelayMs);

                MMDevice device = null;
                try
                {
                    device = _enumerator.GetDevice(spec.DeviceId);
                    if (device == null)
                        throw new InvalidOperationException("dispositivo não encontrado");
                    if ((device.State & DeviceState.Active) == 0)
                        throw new InvalidOperationException("dispositivo inativo");
                }
                catch (Exception ex)
                {
                    DisposeQuietly(device);
                    failures.Add(name + " (" + ex.Message + ")");
                    Diag.Log("falha ao abrir saída " + name + ": " + ex.Message);
                    continue;
                }

                int capacityMs = prebufferMs + delayMs + 100;
                if (capacityMs < 200)
                    capacityMs = 200;

                TargetRing ring = new TargetRing(format, capacityMs, delayMs);
                WasapiOut output = null;
                try
                {
                    output = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
                    output.PlaybackStopped += OnPlaybackStopped;
                    output.Init(ring);
                }
                catch (Exception ex)
                {
                    if (output != null)
                    {
                        output.PlaybackStopped -= OnPlaybackStopped;
                        DisposeQuietly(output);
                    }
                    DisposeQuietly(device);
                    failures.Add(name + " (" + ex.Message + ")");
                    Diag.Log("falha ao abrir saída " + name + ": " + ex.Message);
                    continue;
                }

                TargetOutput target = new TargetOutput();
                target.DeviceId = spec.DeviceId;
                target.Name = name;
                target.Ring = ring;
                target.Output = output;
                target.Device = device;
                target.Stats = new DeviceStats();
                target.Stats.Name = name;
                target.Stats.TargetBufferMs = prebufferMs;
                target.Stats.Alive = false;
                target.LastDriftLogTicks = DateTime.UtcNow.Ticks;
                _targetList.Add(target);
            }

            if (_targetList.Count == 0)
                throw new InvalidOperationException("Não foi possível abrir nenhum dispositivo de saída. " + Join(failures));

            if (failures.Count > 0)
            {
                string warning = "Aviso: algumas saídas não abriram (" + Join(failures) + ")";
                _lastError = warning;
                Diag.Log(warning);
            }

            _published = _targetList.ToArray();

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            try
            {
                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                throw new InvalidOperationException("Não foi possível iniciar a captura de áudio: " + ex.Message);
            }

            _running = true;
            Diag.Log("captura iniciada: fonte=" + _sourceName + ", mix=" + _mixFormat +
                     ", saídas=" + _targetList.Count + ", pré-buffer=" + prebufferMs + "ms");
        }

        private void PlayAll()
        {
            int started = 0;
            List<string> failures = new List<string>();

            for (int i = 0; i < _targetList.Count; i++)
            {
                TargetOutput target = _targetList[i];
                try
                {
                    target.Output.Play();
                    target.Stats.Alive = true;
                    target.Stats.LastError = null;
                    target.Restarts = 0;
                    started++;
                }
                catch (Exception ex)
                {
                    target.Stats.Alive = false;
                    target.Stats.LastError = ex.Message;
                    failures.Add(target.Name + " (" + ex.Message + ")");
                    Diag.Log("falha ao iniciar saída " + target.Name + ": " + ex.Message);
                }
            }

            if (started == 0)
                throw new InvalidOperationException("Nenhuma saída pôde iniciar a reprodução. " + Join(failures));

            if (failures.Count > 0)
            {
                string warning = "Aviso: algumas saídas não iniciaram (" + Join(failures) + ")";
                _lastError = warning;
                Diag.Log(warning);
            }

            StartMaintenance();
            Diag.Log("reprodução iniciada em " + started + " saída(s)");
        }

        private void WaitForPrebuffer(int prebufferMs)
        {
            DateTime start = DateTime.UtcNow;
            double nextLogMs = 5000;
            while (true)
            {
                if (!_running || _disposed)
                    return;

                TargetOutput[] targets = _published;
                if (targets.Length == 0)
                    return;

                bool ready = true;
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i].Ring.AvailableMs < prebufferMs)
                    {
                        ready = false;
                        break;
                    }
                }

                if (ready)
                {
                    Diag.Log("pré-buffer completo (" + prebufferMs + " ms)");
                    return;
                }

                double elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                if (elapsed >= nextLogMs)
                {
                    nextLogMs += 5000;
                    if (!_hasCapturedAudio)
                        Diag.Log("aguardando áudio no dispositivo fonte para completar o pré-buffer (" +
                                 (long)(elapsed / 1000) + " s)");
                    else
                        Diag.Log("pré-buffer em andamento (" + (long)(elapsed / 1000) + " s, " +
                                 targets[0].Ring.AvailableMs.ToString("F1") + " ms)");
                }

                Thread.Sleep(5);
            }
        }

        private void StopInternal()
        {
            WasapiCapture capture = null;
            List<TargetOutput> targets = null;
            Thread maintenance = null;
            Thread teardown = null;

            lock (_sync)
            {
                _running = false;
                _maintenanceRunning = false;
                _maintenanceSession = _maintenanceSession + 1;

                capture = _capture;
                _capture = null;

                if (_targetList.Count > 0)
                {
                    targets = new List<TargetOutput>(_targetList);
                    _targetList.Clear();
                }
                _published = new TargetOutput[0];
                _sourceDevice = null;

                maintenance = _maintenanceThread;
                _maintenanceThread = null;

                if (capture != null || targets != null)
                {
                    WasapiCapture cap = capture;
                    List<TargetOutput> list = targets;
                    teardown = new Thread(delegate() { TearDown(cap, list); });
                    teardown.IsBackground = true;
                    teardown.Name = "DualAudioMirror.Stop";
                    _teardownThread = teardown;
                }
            }

            if (maintenance != null && maintenance != Thread.CurrentThread)
            {
                try
                {
                    if (!maintenance.Join(800))
                        Diag.Log("fila de deriva ainda encerrando");
                }
                catch (Exception ex)
                {
                    Diag.Log("parar manutenção: " + ex.Message);
                }
            }

            if (teardown != null)
            {
                try
                {
                    teardown.Start();
                }
                catch (Exception ex)
                {
                    Diag.Log("falha ao iniciar a paragem: " + ex.Message);
                    return;
                }

                try
                {
                    if (!teardown.Join(2500))
                        Diag.Log("paragem excedeu o tempo limite; continuando em segundo plano");
                }
                catch (Exception ex)
                {
                    Diag.Log("aguardar paragem: " + ex.Message);
                }
            }
        }

        private void TearDown(WasapiCapture capture, List<TargetOutput> targets)
        {
            lock (_sync)
            {
                try
                {
                    if (capture != null)
                    {
                        try { capture.DataAvailable -= OnDataAvailable; } catch (Exception) { }
                        try { capture.RecordingStopped -= OnRecordingStopped; } catch (Exception) { }
                        try { capture.StopRecording(); }
                        catch (Exception ex) { Diag.Log("parar captura: " + ex.Message); }
                        try { capture.Dispose(); }
                        catch (Exception ex) { Diag.Log("liberar captura: " + ex.Message); }

                        Diag.Log("captura encerrada: " +
                                 (Interlocked.Read(ref _capturedBytes) / 1024.0).ToString("F1") + " KB, " +
                                 Interlocked.Read(ref _emptyPackets) + " pacotes vazios");
                    }

                    if (targets != null)
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            TargetOutput target = targets[i];
                            target.Stats.Alive = false;
                            if (target.Output != null)
                            {
                                try { target.Output.PlaybackStopped -= OnPlaybackStopped; } catch (Exception) { }
                                try { target.Output.Stop(); }
                                catch (Exception ex) { Diag.Log("parar saída " + target.Name + ": " + ex.Message); }
                                try { target.Output.Dispose(); }
                                catch (Exception ex) { Diag.Log("liberar saída " + target.Name + ": " + ex.Message); }
                                target.Output = null;
                            }
                            if (target.Device != null)
                            {
                                DisposeQuietly(target.Device);
                                target.Device = null;
                            }
                        }
                    }

                    Diag.Log("motor parado");
                }
                catch (Exception ex)
                {
                    Diag.Log("paragem: " + ex.Message);
                }
            }
        }

        private void WaitForPendingTeardown()
        {
            Thread thread;
            lock (_sync)
            {
                thread = _teardownThread;
                _teardownThread = null;
            }

            if (thread == null || thread == Thread.CurrentThread)
                return;

            try
            {
                if (!thread.Join(8000))
                    Diag.Log("paragem anterior ainda em andamento");
            }
            catch (Exception ex)
            {
                Diag.Log("aguardar paragem anterior: " + ex.Message);
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                int bytes = e.BytesRecorded;
                if (bytes <= 0)
                {
                    Interlocked.Increment(ref _emptyPackets);
                    return;
                }

                Interlocked.Add(ref _capturedBytes, bytes);
                _hasCapturedAudio = true;

                TargetOutput[] targets = _published;
                for (int i = 0; i < targets.Length; i++)
                    targets[i].Ring.Put(e.Buffer, 0, bytes);
            }
            catch (Exception ex)
            {
                Diag.Log("dados da captura: " + ex.Message);
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                if (!_running)
                    return;
                if (e == null || e.Exception == null)
                    return;

                string message = "A captura de áudio parou: " + e.Exception.Message;
                Thread thread = new Thread(delegate() { RaiseError(message); });
                thread.IsBackground = true;
                thread.Name = "DualAudioMirror.CaptureError";
                thread.Start();
            }
            catch (Exception ex)
            {
                Diag.Log("parada da captura: " + ex.Message);
            }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                if (!_running)
                    return;

                WasapiOut failed = sender as WasapiOut;
                if (failed == null)
                    return;

                string message = e != null && e.Exception != null
                    ? e.Exception.Message
                    : "a saída parou inesperadamente (fim dos dados)";

                Thread thread = new Thread(delegate() { HandleOutputFailure(failed, message); });
                thread.IsBackground = true;
                thread.Name = "DualAudioMirror.Restart";
                thread.Start();
            }
            catch (Exception ex)
            {
                Diag.Log("parada da saída: " + ex.Message);
            }
        }

        private void HandleOutputFailure(WasapiOut failed, string message)
        {
            try
            {
                TargetOutput target = FindTarget(failed);
                if (target == null)
                    return;

                string name = target.Name;

                lock (_sync)
                {
                    if (!_running || _disposed)
                        return;
                    if (!IsTargetListed(target))
                        return;
                    target.Stats.Alive = false;
                    target.Stats.LastError = message;
                }

                Diag.Log("saída \"" + name + "\" parou: " + message);

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    Thread.Sleep(500);

                    bool reopened = false;
                    lock (_sync)
                    {
                        if (!_running || _disposed)
                            return;
                        if (!IsTargetListed(target))
                            return;
                        if (target.Output != null && target.Output != failed)
                            return;
                        reopened = ReopenOutput(target, attempt);
                    }

                    if (reopened)
                    {
                        Diag.Log("saída \"" + name + "\" reaberta na tentativa " + attempt);
                        return;
                    }
                }

                RaiseError("Não foi possível reabrir \"" + name + "\" após 3 tentativas: " + message);
            }
            catch (Exception ex)
            {
                Diag.Log("reinício de saída: " + ex.Message);
            }
        }

        private bool ReopenOutput(TargetOutput target, int attempt)
        {
            WasapiOut previous = target.Output;
            target.Output = null;
            if (previous != null)
            {
                try { previous.PlaybackStopped -= OnPlaybackStopped; } catch (Exception) { }
                try { previous.Stop(); } catch (Exception) { }
                try { previous.Dispose(); }
                catch (Exception ex) { Diag.Log("liberar saída antiga " + target.Name + ": " + ex.Message); }
            }

            WasapiOut created = null;
            try
            {
                if (target.Device == null)
                    target.Device = _enumerator.GetDevice(target.DeviceId);

                created = new WasapiOut(target.Device, AudioClientShareMode.Shared, true, _outputLatencyMs);
                created.PlaybackStopped += OnPlaybackStopped;
                created.Init(target.Ring);
                created.Play();

                target.Output = created;
                target.Stats.Alive = true;
                target.Stats.LastError = null;
                target.Restarts = target.Restarts + 1;
                return true;
            }
            catch (Exception ex)
            {
                if (created != null)
                {
                    try { created.PlaybackStopped -= OnPlaybackStopped; } catch (Exception) { }
                    DisposeQuietly(created);
                }
                target.Output = null;
                target.Stats.Alive = false;
                target.Stats.LastError = ex.Message;
                Diag.Log("falha ao reabrir " + target.Name + " (tentativa " + attempt + "): " + ex.Message);
                return false;
            }
        }

        private void StartMaintenance()
        {
            _maintenanceRunning = true;
            int session = _maintenanceSession + 1;
            _maintenanceSession = session;

            Thread thread = new Thread(delegate() { MaintenanceLoop(session); });
            thread.IsBackground = true;
            thread.Name = "DualAudioMirror.Maintenance";
            _maintenanceThread = thread;
            try
            {
                thread.Start();
            }
            catch (Exception ex)
            {
                _maintenanceThread = null;
                Diag.Log("iniciar manutenção: " + ex.Message);
            }
        }

        private void MaintenanceLoop(int session)
        {
            while (_maintenanceRunning && _maintenanceSession == session)
            {
                try
                {
                    Thread.Sleep(250);
                }
                catch (Exception)
                {
                    return;
                }

                if (!_maintenanceRunning || _maintenanceSession != session)
                    return;

                try
                {
                    TargetOutput[] targets = _published;
                    int targetBufferMs = _prebufferMs;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        TargetOutput target = targets[i];
                        long dropped = target.Ring.AdjustDrift(targetBufferMs);
                        if (dropped <= 0)
                            continue;

                        long now = DateTime.UtcNow.Ticks;
                        if (now - target.LastDriftLogTicks < TimeSpan.TicksPerSecond * 10)
                            continue;
                        target.LastDriftLogTicks = now;
                        Diag.Log("deriva corrigida em " + target.Name + " (buffer " +
                                 target.Ring.BufferMs.ToString("F1") + " ms)");
                    }
                }
                catch (Exception ex)
                {
                    Diag.Log("manutenção: " + ex.Message);
                }
            }
        }

        private TargetOutput FindTarget(WasapiOut output)
        {
            TargetOutput[] targets = _published;
            for (int i = 0; i < targets.Length; i++)
            {
                if (ReferenceEquals(targets[i].Output, output))
                    return targets[i];
            }
            return null;
        }

        private bool IsTargetListed(TargetOutput target)
        {
            for (int i = 0; i < _targetList.Count; i++)
            {
                if (ReferenceEquals(_targetList[i], target))
                    return true;
            }
            return false;
        }

        private double UpdateRate()
        {
            long total = Interlocked.Read(ref _capturedBytes);
            long now = DateTime.UtcNow.Ticks;

            lock (_rateLock)
            {
                if (_rateTicks == 0)
                {
                    _rateTicks = now;
                    _rateBytes = total;
                    return _rateKbPerSec;
                }

                double seconds = (now - _rateTicks) / (double)TimeSpan.TicksPerSecond;
                if (seconds >= 0.4)
                {
                    long delta = total - _rateBytes;
                    if (delta < 0)
                        delta = 0;
                    _rateKbPerSec = delta / 1024.0 / seconds;
                    _rateTicks = now;
                    _rateBytes = total;
                }

                return _rateKbPerSec;
            }
        }

        private void TrySetDefault(string deviceId)
        {
            try
            {
                string error;
                if (DevicePolicy.TrySetDefault(deviceId, out error))
                {
                    Diag.Log("dispositivo padrão do Windows atualizado");
                }
                else
                {
                    Diag.Log("aviso: não foi possível definir o dispositivo padrão: " + error);
                }
            }
            catch (Exception ex)
            {
                Diag.Log("aviso: erro ao definir o dispositivo padrão: " + ex.Message);
            }
        }

        private void RaiseError(string message)
        {
            Diag.Log(message);
            _lastError = message;

            Action<string> handler = ErrorOccurred;
            if (handler == null)
                return;

            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                Diag.Log("erro ao notificar o ouvinte: " + ex.Message);
            }
        }

        private static DeviceStats CopyStats(TargetOutput target)
        {
            DeviceStats live = target.Stats;
            DeviceStats copy = new DeviceStats();
            copy.Name = live.Name;
            copy.TargetBufferMs = live.TargetBufferMs;
            copy.Alive = live.Alive;
            copy.LastError = live.LastError;
            copy.BufferMs = target.Ring.BufferMs;
            copy.Underruns = target.Ring.Underruns;
            copy.Overflows = target.Ring.Overflows;
            copy.DriftCorrections = target.Ring.DriftCorrections;
            copy.BytesPlayed = target.Ring.BytesPlayed;
            return copy;
        }

        private static string SafeName(MMDevice device)
        {
            try
            {
                string name = device.FriendlyName;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch (Exception)
            {
            }

            try
            {
                return device.ID;
            }
            catch (Exception)
            {
                return "(dispositivo desconhecido)";
            }
        }

        private static string Join(List<string> items)
        {
            if (items == null || items.Count == 0)
                return string.Empty;
            return string.Join(" | ", items);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static void DisposeQuietly(IDisposable value)
        {
            if (value == null)
                return;
            try
            {
                value.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private sealed class TargetRing : IWaveProvider
        {
            private readonly object _lock = new object();
            private readonly byte[] _buffer;
            private readonly int _capacity;
            private readonly int _blockAlign;
            private readonly WaveFormat _format;
            private readonly double _bytesPerMs;
            private long _head;
            private long _tail;
            private long _delayTotal;
            private long _delayRemaining;
            private long _underruns;
            private long _overflows;
            private long _driftCorrections;
            private long _bytesPlayed;

            public TargetRing(WaveFormat format, int capacityMs, int delayMs)
            {
                _format = format;
                _blockAlign = format.BlockAlign > 0 ? format.BlockAlign : 1;
                _bytesPerMs = format.AverageBytesPerSecond / 1000.0;

                long capacity = (long)(_bytesPerMs * capacityMs);
                long minimum = _blockAlign * 64L;
                if (capacity < minimum)
                    capacity = minimum;
                capacity = ((capacity + _blockAlign - 1) / _blockAlign) * _blockAlign;
                _capacity = (int)capacity;
                _buffer = new byte[_capacity];

                long delay = (long)(_bytesPerMs * delayMs);
                delay = (delay / _blockAlign) * _blockAlign;
                _delayTotal = delay;
                _delayRemaining = delay;
            }

            public WaveFormat WaveFormat
            {
                get { return _format; }
            }

            public long Underruns
            {
                get { lock (_lock) { return _underruns; } }
            }

            public long Overflows
            {
                get { lock (_lock) { return _overflows; } }
            }

            public long DriftCorrections
            {
                get { lock (_lock) { return _driftCorrections; } }
            }

            public long BytesPlayed
            {
                get { lock (_lock) { return _bytesPlayed; } }
            }

            public double AvailableMs
            {
                get { lock (_lock) { return (_tail - _head) * 1000.0 / _format.AverageBytesPerSecond; } }
            }

            public double BufferMs
            {
                get { lock (_lock) { return EffectiveMs(); } }
            }

            public void Put(byte[] source, int offset, int count)
            {
                if (source == null || count <= 0)
                    return;

                lock (_lock)
                {
                    if (count > _capacity)
                    {
                        offset += count - _capacity;
                        count = _capacity;
                    }

                    long fill = _tail - _head;
                    if (fill + count > _capacity)
                    {
                        long excess = fill + count - _capacity;
                        long drop = ((excess + _blockAlign - 1) / _blockAlign) * _blockAlign;
                        long available = (fill / _blockAlign) * _blockAlign;
                        if (drop > available)
                            drop = available;
                        _head += drop;
                        if (_head > _tail)
                            _head = _tail;
                        _overflows++;
                    }

                    int position = offset;
                    int remaining = count;
                    while (remaining > 0)
                    {
                        int index = (int)(_tail % _capacity);
                        int chunk = Math.Min(remaining, _capacity - index);
                        Array.Copy(source, position, _buffer, index, chunk);
                        _tail += chunk;
                        position += chunk;
                        remaining -= chunk;
                    }
                }
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null || count <= 0)
                    return 0;

                lock (_lock)
                {
                    int written = 0;

                    if (_delayRemaining > 0)
                    {
                        int silence = (int)Math.Min(_delayRemaining, count);
                        Array.Clear(buffer, offset, silence);
                        _delayRemaining -= silence;
                        written = silence;
                    }

                    while (written < count && _tail > _head)
                    {
                        int index = (int)(_head % _capacity);
                        long available = _tail - _head;
                        int room = _capacity - index;
                        int want = count - written;
                        int chunk = (int)Math.Min(want, Math.Min(room, available));
                        if (chunk <= 0)
                            break;
                        Array.Copy(_buffer, index, buffer, offset + written, chunk);
                        _head += chunk;
                        written += chunk;
                    }

                    if (written < count)
                    {
                        Array.Clear(buffer, offset + written, count - written);
                        _underruns++;
                    }

                    _bytesPlayed += count;
                }

                return count;
            }

            public long AdjustDrift(int targetBufferMs)
            {
                lock (_lock)
                {
                    double bufferMs = EffectiveMs();
                    if (bufferMs <= targetBufferMs + 20)
                        return 0;

                    double excess = bufferMs - targetBufferMs;
                    long drop = (long)(_bytesPerMs * excess);
                    long maximum = (long)(_bytesPerMs * 5);
                    if (maximum < _blockAlign)
                        maximum = _blockAlign;
                    if (drop > maximum)
                        drop = maximum;
                    drop = ((drop + _blockAlign - 1) / _blockAlign) * _blockAlign;

                    long available = (_tail - _head) / _blockAlign * _blockAlign;
                    if (drop > available)
                        drop = available;
                    if (drop <= 0)
                        return 0;

                    _head += drop;
                    if (_head > _tail)
                        _head = _tail;
                    _overflows++;
                    _driftCorrections++;
                    return drop;
                }
            }

            private double EffectiveMs()
            {
                long emittedDelay = _delayTotal - _delayRemaining;
                long effective = (_tail - _head) - emittedDelay;
                if (effective < 0)
                    effective = 0;
                return effective * 1000.0 / _format.AverageBytesPerSecond;
            }
        }
    }

    public static class Diag
    {
        private const int MaxEntries = 500;
        private static readonly object Sync = new object();
        private static readonly Queue<string> Entries = new Queue<string>();
        private static string _logFilePath;

        public static string LogFilePath
        {
            get
            {
                lock (Sync)
                {
                    return EnsurePath();
                }
            }
        }

        public static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + (message ?? string.Empty);
            try
            {
                lock (Sync)
                {
                    Entries.Enqueue(line);
                    while (Entries.Count > MaxEntries)
                        Entries.Dequeue();

                    string path = EnsurePath();
                    if (path != null)
                        File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }

        public static IReadOnlyList<string> Recent(int max)
        {
            lock (Sync)
            {
                List<string> list = new List<string>(Entries);
                if (max <= 0)
                    return new string[0];
                if (list.Count > max)
                    list.RemoveRange(0, list.Count - max);
                return list;
            }
        }

        private static string EnsurePath()
        {
            if (_logFilePath != null)
                return _logFilePath;

            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DualAudioMirror");
                Directory.CreateDirectory(directory);
                _logFilePath = Path.Combine(directory, "DualAudioMirror.log");
            }
            catch (Exception)
            {
                _logFilePath = null;
            }

            return _logFilePath;
        }
    }
}
