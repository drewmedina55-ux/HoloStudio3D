using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

public sealed class WindowsImapiBurner : IOpticalDiscBurner
{
    private readonly OpticalBurnOperationGate _operationGate = new();
    private const int CdRawSubcodeIsCooked = 2;
    private const int CdDaoFallbackSpeedSectorsPerSecond = 1200;
    // DAO avoids the two-sector TAO track overhead observed on the tested drive.
    // Keep only a very small physical overburn allowance for nearby measured presets.
    private const long CdDaoOverburnAllowanceSectors = 3;

    private const int BlankMediaState = 0x2;
    // IMAPI_MEDIA_PHYSICAL_TYPE and IMAPI_PROFILE_TYPE (imapi2.h).
    // https://learn.microsoft.com/windows/win32/api/imapi2/ne-imapi2-imapi_media_physical_type
    // https://learn.microsoft.com/windows/win32/api/imapi2/ne-imapi2-imapi_profile_type
    private const int BluRayRecordableMedia = 0x12;
    private const int BluRaySequentialProfile = 0x41;
    private const uint BluRaySingleLayerSectors = 12_219_392;

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
        => _operationGate.RunAsync(() => RunOnStaThread(() => EnumerateDevices(cancellationToken)),
            cancellationToken);

    public Task<long> GetMediaSectorCountAsync(string deviceId, OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        _ = OpticalBurnMedia.GetSectorBytes(mediaKind);
        return _operationGate.RunAsync(() => RunOnStaThread(() =>
            ReadMediaSectorCount(deviceId, mediaKind, cancellationToken)), cancellationToken);
    }

    private static long ReadMediaSectorCount(string deviceId, OpticalBurnMediaKind mediaKind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        dynamic? recorder = null;
        dynamic? format = null;
        try
        {
            recorder = CreateRecorder(deviceId);
            format = CreateComObject("IMAPI2.MsftDiscFormat2Data");
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            ValidateRequestedMedia(mediaKind, recorder, format);
            // These are media queries. PrepareMedia would enter a recording session.
            long sectors;
            if (mediaKind == OpticalBurnMediaKind.DvdData)
            {
                // Includes used sectors. Never use FreeSectorsOnMedia or a mounted volume's length.
                sectors = ReadDvdSectorCount(Convert.ToInt32(format.CurrentPhysicalMediaType),
                    Convert.ToInt64(format.TotalSectorsOnMedia));
            }
            else
            {
                var transport = new ImapiMmcCommandTransport((object)recorder);
                sectors = OpticalBurnMedia.UsesCdDao(mediaKind)
                    ? ReadCdAtipSectorCount(transport)
                    : ReadBluRaySectorCount(transport);
            }
            token.ThrowIfCancellationRequested();
            return sectors;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0xC0AA0202))
        {
            throw new InvalidOperationException("There is no disc in the selected drive.", ex);
        }
        finally
        {
            if (format is not null) ReleaseComObject(format);
            if (recorder is not null) ReleaseComObject(recorder);
        }
    }

    internal static long ReadCdAtipSectorCount(IMmcCommandTransport transport)
    {
        // READ TOC/PMA/ATIP format 4. Unlike a session TOC, the pre-groove lead-out
        // describes the recordable disc even if only part of it has been written.
        const byte allocation = 64;
        byte[] cdb = [0x43, 0x02, 0x04, 0, 0, 0, 0, 0, allocation, 0];
        MmcCommandResult response = transport.Execute(cdb, allocation, 30);
        if (response.HResult < 0 || response.HResult == BluRayMmcCommands.CommandHasSenseData)
            throw new MmcCommandException(cdb[0], response);
        byte[] data = response.Data;
        if (data.Length < 15)
            throw new IOException("The CD pre-groove lead-out information returned by the drive is incomplete.");
        int declared = BinaryPrimitives.ReadUInt16BigEndian(data) + 2;
        if (declared < 15 || data.Length < Math.Min(allocation, declared))
            throw new IOException("The CD pre-groove lead-out information returned by the drive is incomplete.");
        int minutes = data[12], seconds = data[13], frames = data[14];
        // Binary MSF. LBA 0 starts at 00:02:00; the lead-out address is exclusive.
        int count = ((minutes * 60 + seconds) * 75 + frames) - 150;
        if (minutes >= 100 || seconds >= 60 || frames >= 75 || count <= 0)
            throw new IOException("The CD lead-out address returned by the drive is invalid.");
        return count;
    }

    internal static long ReadBluRaySectorCount(IMmcCommandTransport transport)
    {
        var (blocks, status, spareClusters) = new BluRayMmcCommands(transport).ReadFormatCapacity();
        // Type 1 counts data-zone blocks; spareClusters is the possible maximum.
        // Type 2 counts user blocks; spareClusters is actually allocated space.
        // Smaller drawing ranges can be authored, but the recorder requires the
        // complete physical layout of the supported no-spare 25 GB single layer.
        if (status is not (1 or 2))
            throw new InvalidOperationException("The drive did not report a valid capacity for the current BD-R.");
        if (status == 2 && spareClusters != 0)
            throw new InvalidOperationException("The BD-R has a spare area allocated; its logical capacity cannot be used for no-spare-area calibration.");
        if (blocks != BluRaySingleLayerSectors)
            throw new InvalidOperationException($"The BD-R reports {blocks:N0} sectors; calibration currently supports only the 25 GB single-layer no-spare-area layout of 12,219,392 sectors.");
        return blocks;
    }

    internal static long ReadDvdSectorCount(int physicalMediaType, long totalSectorsOnMedia)
    {
        if (physicalMediaType is 0x8 or 0xB or 0xD)
            throw new InvalidOperationException("Dual-layer DVDs cannot be used for single-layer disc calibration.");
        if (totalSectorsOnMedia <= 0)
            throw new InvalidOperationException("The drive did not report a valid total DVD sector count.");
        return totalSectorsOnMedia;
    }

    public Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(
        string deviceId,
        OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        _ = OpticalBurnMedia.GetSectorBytes(mediaKind);
        return _operationGate.RunAsync(() => RunOnStaThread(() => EnumerateWriteSpeeds(
            deviceId,
            mediaKind,
            cancellationToken)), cancellationToken);
    }

    public Task<OpticalBurnResult> BurnAsync(
        OpticalBurnRequest request,
        IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return _operationGate.RunAsync(() => RunOnStaThread(() => Burn(request, progress, cancellationToken)),
            cancellationToken);
    }

    private static IReadOnlyList<OpticalBurnDevice> EnumerateDevices(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dynamic master = CreateComObject("IMAPI2.MsftDiscMaster2");
        try
        {
            if (!(bool)master.IsSupportedEnvironment)
            {
                return [];
            }

            int count = (int)master.Count;
            List<OpticalBurnDevice> devices = new(count);
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = (string)master.Item(index);
                dynamic recorder = CreateRecorder(id);
                try
                {
                    string vendor = Convert.ToString(recorder.VendorId)?.Trim() ?? string.Empty;
                    string product = Convert.ToString(recorder.ProductId)?.Trim() ?? string.Empty;
                    IReadOnlyList<string> volumes = ReadStrings(recorder.VolumePathNames);
                    string drive = volumes.Count == 0 ? string.Empty : $" ({string.Join(", ", volumes)})";
                    string model = string.Join(" ", new[] { vendor, product }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                    devices.Add(new OpticalBurnDevice(
                        id,
                        (model.Length == 0 ? $"Drive {index + 1}" : model) + drive,
                        volumes));
                }
                finally
                {
                    ReleaseComObject(recorder);
                }
            }

            return devices;
        }
        finally
        {
            ReleaseComObject(master);
        }
    }

    private static IReadOnlyList<OpticalWriteSpeed> EnumerateWriteSpeeds(
        string deviceId,
        OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dynamic recorder = CreateRecorder(deviceId);
        dynamic? format = null;
        try
        {
            format = OpticalBurnMedia.UsesCdDao(mediaKind)
                ? CreateComObject("IMAPI2.MsftDiscFormat2RawCD")
                : CreateComObject("IMAPI2.MsftDiscFormat2Data");
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            if (!(bool)format.IsRecorderSupported(recorder))
            {
                throw new InvalidOperationException("The selected drive does not support this burn method.");
            }

            if (!(bool)format.IsCurrentMediaSupported(recorder))
            {
                throw new InvalidOperationException("Insert a writable disc compatible with the selected CD/DVD/Blu-ray mode.");
            }

            ValidateRequestedMedia(mediaKind, recorder, format);
            cancellationToken.ThrowIfCancellationRequested();
            return ReadSupportedWriteSpeeds(format);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Failed to read the burn speeds for the current disc (0x{exception.HResult:X8}): {exception.Message}",
                exception);
        }
        finally
        {
            if (format is not null)
            {
                ReleaseComObject(format);
            }

            ReleaseComObject(recorder);
        }
    }

    private static OpticalBurnResult Burn(
        OpticalBurnRequest request,
        IProgress<OpticalBurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ImapiWriteProgressReporter writeProgress = new(request.MediaKind, request.ContentLength,
            value => progress?.Report(value));
        string currentOperation = "Initialize Windows burning interface";
        List<string> cleanupWarnings = [];
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OpticalBurnProgress(
            "device",
            "Checking recorder and blank media…",
            0,
            request.ContentLength));
        dynamic recorder = CreateRecorder(request.DeviceId);
        dynamic? format = null;
        OpticalBurnResult? result = null;
        Exception? burnFailure = null;
        try
        {
            currentOperation = "Create burn format";
            format = OpticalBurnMedia.UsesCdDao(request.MediaKind)
                ? CreateComObject("IMAPI2.MsftDiscFormat2RawCD")
                : CreateComObject("IMAPI2.MsftDiscFormat2Data");
            currentOperation = "Connect to recorder";
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            currentOperation = "Check recorder support";
            if (!(bool)format.IsRecorderSupported(recorder))
            {
                throw new InvalidOperationException("The selected drive does not support this burn method.");
            }

            currentOperation = "Check current disc compatibility";
            if (!(bool)format.IsCurrentMediaSupported(recorder))
            {
                throw new InvalidOperationException("The current disc is not compatible with the selected CD/DVD/Blu-ray mode.");
            }

            ValidateRequestedMedia(request.MediaKind, recorder, format);
            if (request.WriteSpeed is null)
            {
                currentOperation = "Select lowest burn speed";
                request = request with
                {
                    WriteSpeed = ResolveWriteSpeed(
                        null,
                        ReadSupportedWriteSpeeds(format)),
                };
                progress?.Report(new OpticalBurnProgress(
                    "speed",
                    request.WriteSpeed is null
                        ? "The current disc reports no selectable speeds; using the burning interface's conservative fallback…"
                        : $"Automatically selected lowest write speed: "
                            + $"{request.WriteSpeed.GetMultiplier(request.MediaKind):0.#}×"
                            + $" ({request.WriteSpeed.GetMegabytesPerSecond(request.MediaKind):0.0} MB/s)…",
                    0,
                    request.ContentLength));
            }

            using GeneratedContentComStream content = new(
                request.ContentLength,
                request.ProduceContent,
                (completed, total) => progress?.Report(new OpticalBurnProgress(
                    "transfer",
                    $"Transferring {completed / (1024.0 * 1024.0):F1} / {total / (1024.0 * 1024.0):F1} MiB to the burning interface…",
                    completed,
                    total)),
                cancellationToken,
                request.EffectiveBufferOptions);
            progress?.Report(new OpticalBurnProgress(
                "buffer",
                "Preparing the underrun protection buffer…",
                0,
                request.ContentLength));
            content.WaitUntilPrebuffered(cancellationToken);
            // Preparation may take time. Recheck the loaded medium before writing.
            ValidateRequestedMedia(request.MediaKind, recorder, format);

            OpticalWriteSpeed? actualWriteSpeed;
            if (OpticalBurnMedia.UsesCdDao(request.MediaKind))
            {
                using ICdRawImageSession imageSession = request.MediaKind == OpticalBurnMediaKind.CdData
                    ? new CdDataRawImageSession() : new DynamicCdRawImageSession();
                actualWriteSpeed = BurnCdDao(
                    new DynamicCdRawSession((object)format, writeProgress.ReportRawCdUpdate),
                    imageSession,
                    content,
                    request,
                    cancellationToken,
                    operation =>
                    {
                        currentOperation = operation;
                        if (operation == "Close CD burn session")
                            writeProgress.ReportStage("finalize", "Closing CD burn session…");
                    },
                    speed => ReportWriteSpeed(progress, speed, request));
            }
            else if (request.MediaKind == OpticalBurnMediaKind.BluRayData)
            {
                // IDiscFormat2Data.Write formats virgin BD-R with spare areas.
                // The lower-level write engine only sends WRITE commands, so
                // the unformatted track keeps the LBA/physical-address mapping.
                using DynamicBluRaySequentialSession session = new((object)recorder, (object)format,
                    writeProgress.ReportAcceptedBytes,
                    reportCleanupWarning: message => cleanupWarnings.Add(message),
                    reportDiagnostics: message => progress?.Report(new OpticalBurnProgress(
                        "diagnostic", message, 0, request.ContentLength)));
                actualWriteSpeed = BurnBluRay(session, content, request,
                    cancellationToken, operation =>
                    {
                        currentOperation = operation;
                        bool finalizing = operation.Contains("Finalize disc", StringComparison.Ordinal);
                        writeProgress.ReportStage(finalizing ? "finalize" : "media", operation);
                    },
                    speed => ReportWriteSpeed(progress, speed, request),
                    reportCleanupWarning: cleanupWarnings.Add);
            }
            else
            {
                currentOperation = "Write DVD data";
                Action<OpticalWriteSpeed> reportDataWriteSpeed =
                    speed => ReportWriteSpeed(progress, speed, request);
                actualWriteSpeed = BurnData(
                    (object)format,
                    content,
                    request,
                    cancellationToken,
                    reportDataWriteSpeed,
                    writeProgress.ReportDataUpdate);
            }

            stopwatch.Stop();
            result = new OpticalBurnResult(
                request.DeviceId,
                request.MediaKind,
                request.ContentLength,
                stopwatch.Elapsed,
                actualWriteSpeed);
        }
        catch (COMException exception)
        {
            burnFailure = new InvalidOperationException(
                $"The Windows burning interface returned an error during \"{currentOperation}\" "
                + $"0x{exception.HResult:X8}: {exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            burnFailure = exception;
        }
        finally
        {
            CompleteBurnCleanup(burnFailure, cleanupWarnings,
                () => { if (format is not null) ReleaseComObject((object)format); },
                () => ReleaseComObject((object)recorder));
        }

        progress?.Report(new OpticalBurnProgress(
            "complete",
            "Burn complete; the disc session is closed.",
            request.ContentLength,
            request.ContentLength));
        return result!;
    }

    // The burn exception is captured before entering this final cleanup boundary.
    // Every caller-owned COM object gets its release attempt before we propagate
    // that exception or report an otherwise successful burn's cleanup failure.
    internal static void CompleteBurnCleanup(Exception? burnFailure, ICollection<string> cleanupWarnings,
        Action releaseFormat, Action releaseRecorder)
    {
        Exception? firstCleanupFailure = null;
        ReleaseOne(releaseFormat, "Release burn format COM object");
        ReleaseOne(releaseRecorder, "Release recorder COM object");

        string details = cleanupWarnings.Count == 0 ? string.Empty
            : "\nThe following problems also occurred while releasing the device (further cleanup was attempted):\n"
                + string.Join("\n", cleanupWarnings.Distinct());
        if (burnFailure is not null)
        {
            if (cleanupWarnings.Count > 0)
            {
                if (burnFailure is OperationCanceledException cancellation)
                    throw new OperationCanceledException(burnFailure.Message + details,
                        cancellation, cancellation.CancellationToken);
                throw new InvalidOperationException(burnFailure.Message + details, burnFailure);
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(burnFailure).Throw();
        }
        if (cleanupWarnings.Count > 0)
            throw new InvalidOperationException("The burn finished, but the device was not released cleanly." + details, firstCleanupFailure);

        void ReleaseOne(Action release, string operation)
        {
            try { release(); }
            catch (Exception exception)
            {
                firstCleanupFailure ??= exception;
                cleanupWarnings.Add($"{operation} failed (HRESULT 0x{exception.HResult:X8}): {exception.Message}");
            }
        }
    }

    internal static OpticalWriteSpeed BurnBluRay(
        IBluRaySequentialSession session,
        GeneratedContentComStream content,
        OpticalBurnRequest request,
        CancellationToken cancellationToken,
        Action<string>? reportOperation = null,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null,
        Action<string>? reportCleanupWarning = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);
        request.Validate();
        if (request.MediaKind != OpticalBurnMediaKind.BluRayData)
            throw new ArgumentException("The sequential BD-R channel only accepts Blu-ray data.", nameof(request));

        bool acquired = false;
        bool writeStarted = false;
        bool flushed = false;
        Exception? operationException = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportOperation?.Invoke("Lock BD-R disc");
            session.Acquire();
            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            reportOperation?.Invoke("Check raw capacity of unformatted BD-R");
            BluRaySequentialMediaInfo media = session.ReadMedia();
            ValidateBluRaySequentialMedia(request, media);
            reportOperation?.Invoke("Set BD-R write speed");
            OpticalWriteSpeed speed = session.ConfigureWrite(request.WriteSpeed);
            speed.Validate();
            reportWriteSpeed?.Invoke(speed);
            cancellationToken.ThrowIfCancellationRequested();
            reportOperation?.Invoke($"Sequential no-spare-area BD-R write: {request.ContentLength / 2048:N0} sectors");
            using (cancellationToken.Register(session.CancelWrite))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writeStarted = true;
                session.Write(content, checked((uint)(request.ContentLength / 2048)));
            }
            cancellationToken.ThrowIfCancellationRequested();

            // Once finalization starts, finish flushing/closing before releasing
            // the drive. Cancelling an in-flight CLOSE command is not safe.
            reportOperation?.Invoke("Flush BD-R cache and finalize disc (please wait until complete)");
            session.Flush();
            flushed = true;
            session.CloseTrack(media.TrackNumber);
            session.CloseDisc();
            return speed;
        }
        catch (Exception exception)
        {
            operationException = exception;
            if (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
                throw new OperationCanceledException("Blu-ray burn canceled.", exception, cancellationToken);
            throw;
        }
        finally
        {
            if (acquired)
            {
                if (operationException is null)
                {
                    session.Release();
                }
                else
                {
                    // Preserve the original failure and drain any accepted
                    // blocks, but never finalize a failed or cancelled image.
                    if (writeStarted && !flushed)
                    {
                        long flushStarted = Stopwatch.GetTimestamp();
                        try { session.Flush(); }
                        catch (Exception exception)
                        {
                            TimeSpan elapsed = Stopwatch.GetElapsedTime(flushStarted);
                            try
                            {
                                reportCleanupWarning?.Invoke($"Flushing the cache after the Blu-ray failure also failed (HRESULT 0x{exception.HResult:X8}, "
                                    + $"took {elapsed.TotalSeconds:F3} s): {exception.Message}");
                            }
                            catch { /* Diagnostics must not replace the write failure or prevent release. */ }
                        }
                    }
                    try { session.Release(); }
                    catch { }
                }
            }
        }
    }

    internal static void ValidateBluRaySequentialMedia(
        OpticalBurnRequest request, BluRaySequentialMediaInfo media)
    {
        request.Validate();
        if (request.MediaKind != OpticalBurnMediaKind.BluRayData)
            throw new ArgumentException("The sequential BD-R channel only accepts Blu-ray data.", nameof(request));
        if (media.Profile != BluRaySequentialProfile)
            throw new InvalidOperationException("No-spare-area drawing requires a sequential-recording BD-R (SRM); BD-RE and random-recording BD-R are not supported.");
        if (!media.DiscIsBlank || !media.TrackIsBlank || !media.NextWritableAddressValid
            || media.NextWritableAddress != 0 || media.TrackStartAddress != 0 || media.TrackNumber != 1)
            throw new InvalidOperationException("No-spare-area drawing requires a brand-new blank BD-R starting at LBA 0; the burn was canceled.");
        if (media.FormatStatus != 1)
            throw new InvalidOperationException("The BD-R is formatted or its format state is unknown; the no-spare-area physical position cannot be guaranteed. Use a new unformatted disc.");

        long requiredSectors = request.ContentLength / 2048;
        if (requiredSectors % 32 != 0 || requiredSectors > BluRaySingleLayerSectors)
            throw new InvalidOperationException("The total Blu-ray drawing sector count must be a multiple of 32 and no more than 12,219,392.");
        if (media.FreeBlocks < requiredSectors)
            throw new InvalidOperationException(
                $"Not enough Blu-ray disc capacity: {requiredSectors:N0} sectors needed, raw track can write {media.FreeBlocks:N0} sectors. The drawing size will not be reduced automatically.");
        if (media.MaximumBlocks != BluRaySingleLayerSectors
            || media.TrackSize != media.MaximumBlocks || media.FreeBlocks != media.TrackSize)
            throw new InvalidOperationException(
                $"The disc's raw capacity or track layout does not match a single-layer no-spare-area BD-R: maximum {media.MaximumBlocks:N0}, "
                + $"track {media.TrackSize:N0}, writable {media.FreeBlocks:N0} sectors; the full 12,219,392-sector layout is required.");
    }

    internal static OpticalWriteSpeed BurnCdAudio(
        ICdRawSession format,
        ICdRawImageSession imageSession,
        GeneratedContentComStream content,
        OpticalBurnRequest request,
        CancellationToken cancellationToken,
        Action<string>? reportOperation = null,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null)
        => BurnCdDao(format, imageSession, content, request, cancellationToken, reportOperation, reportWriteSpeed);

    internal static OpticalWriteSpeed BurnCdDao(
        ICdRawSession format,
        ICdRawImageSession imageSession,
        GeneratedContentComStream content,
        OpticalBurnRequest request,
        CancellationToken cancellationToken,
        Action<string>? reportOperation = null,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null)
    {
        if (!OpticalBurnMedia.UsesCdDao(request.MediaKind))
            throw new ArgumentException("CD DAO requires an audio or CD data request.", nameof(request));
        string trackDescription = request.MediaKind == OpticalBurnMediaKind.CdData ? "data track" : "audio track";
        bool prepared = false;
        Exception? operationException = null;
        try
        {
            reportOperation?.Invoke("Prepare and lock CD media");
            format.PrepareMedia();
            prepared = true;
            reportOperation?.Invoke("Check that the CD is blank");
            if (!format.MediaPhysicallyBlank)
            {
                throw new InvalidOperationException("The CD is not blank; the burn was canceled.");
            }

            reportOperation?.Invoke("Set CD DAO sector format");
            if (!format.SupportedSectorTypes.Contains(CdRawSubcodeIsCooked))
            {
                throw new InvalidOperationException(
                    "The selected drive does not support the Cooked R-W subchannel format required for CD DAO.");
            }

            format.SetRequestedSectorType(CdRawSubcodeIsCooked);
            if (request.WriteSpeed is not null)
            {
                reportOperation?.Invoke("Set CD burn speed");
                format.SetWriteSpeed(
                    request.WriteSpeed.SectorsPerSecond,
                    request.WriteSpeed.RotationTypeIsPureCav);
            }
            else
            {
                reportOperation?.Invoke("Use conservative CD DAO fallback speed");
                format.SetWriteSpeed(CdDaoFallbackSpeedSectorsPerSecond, false);
            }

            OpticalWriteSpeed actualWriteSpeed = new(
                format.CurrentWriteSpeed,
                format.CurrentRotationTypeIsPureCav);
            actualWriteSpeed.Validate();
            reportWriteSpeed?.Invoke(actualWriteSpeed);

            reportOperation?.Invoke($"Build CD DAO {trackDescription}");
            IStream rawImage = imageSession.CreateImage(content);
            long startOfLeadout = imageSession.StartOfLeadout;
            long lastPossibleStartOfLeadout = format.LastPossibleStartOfLeadout;
            long excessSectors = startOfLeadout - lastPossibleStartOfLeadout;
            if (excessSectors > CdDaoOverburnAllowanceSectors)
            {
                throw new InvalidOperationException(
                    $"The CD DAO lead-out exceeds the disc boundary by {excessSectors} sectors; "
                    + $"a limited overburn of at most {CdDaoOverburnAllowanceSectors} DAO sectors is allowed.");
            }

            if (excessSectors > 0)
            {
                reportOperation?.Invoke(
                    $"Using CD DAO end overburn ({excessSectors} DAO sectors over)");
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(
                format.CancelWrite);
            reportOperation?.Invoke($"Write CD DAO {trackDescription}");
            format.WriteMedia(rawImage);
            return actualWriteSpeed;
        }
        catch (Exception exception)
        {
            operationException = exception;
            throw;
        }
        finally
        {
            if (prepared)
            {
                if (operationException is null)
                {
                    reportOperation?.Invoke("Close CD burn session");
                    format.ReleaseMedia();
                }
                else
                {
                    TryReleaseCdMedia(format);
                }
            }
        }
    }

    private static void TryReleaseCdMedia(ICdRawSession format)
    {
        try
        {
            format.ReleaseMedia();
        }
        catch
        {
            // Preserve the first failure. Some drives automatically unprepare the
            // media after a write error, making ReleaseMedia fail with 0xC0AA0502.
        }
    }

    private static OpticalWriteSpeed BurnData(
        dynamic format,
        GeneratedContentComStream content,
        OpticalBurnRequest request,
        CancellationToken cancellationToken,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null,
        Action<object>? reportWriteProgress = null)
    {
        ValidateDataMedia(
            request,
            (int)format.CurrentMediaStatus,
            (long)format.FreeSectorsOnMedia);

        format.BufferUnderrunFreeDisabled = false;
        format.ForceMediaToBeClosed = true;
        format.ForceOverwrite = false;
        if (request.WriteSpeed is not null)
        {
            format.SetWriteSpeed(
                request.WriteSpeed.SectorsPerSecond,
                request.WriteSpeed.RotationTypeIsPureCav);
        }

        OpticalWriteSpeed actualWriteSpeed = new(
            Convert.ToInt32(format.CurrentWriteSpeed),
            Convert.ToBoolean(format.CurrentRotationTypeIsPureCAV));
        actualWriteSpeed.Validate();
        reportWriteSpeed?.Invoke(actualWriteSpeed);
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            TryCancel(format, cdAudio: false));
        // DDiscFormat2DataEvents.Update supplies device-written LBAs, unlike
        // reads from the generated source stream. Preserve the cancellation
        // registration above and keep telemetry limited to the Write lifetime.
        Guid events = new("2735413C-7F64-5B0F-8F00-5D77AFBE261E");
        Action<object, object> update = (_, args) => reportWriteProgress?.Invoke(args);
        bool subscribed = false;
        try
        {
            ComEventsHelper.Combine((object)format, events, 0x200, update);
            subscribed = true;
        }
        catch (COMException) { /* Optional telemetry must not prevent writing. */ }
        try { format.Write(content); }
        finally
        {
            if (subscribed)
            {
                try { ComEventsHelper.Remove((object)format, events, 0x200, update); }
                catch (COMException) { /* Preserve any write error. */ }
            }
        }
        return actualWriteSpeed;
    }

    private static void ValidateRequestedMedia(
        OpticalBurnMediaKind mediaKind,
        dynamic recorder,
        dynamic format)
    {
        object currentProfiles = recorder.CurrentProfiles;
        int[] profiles = currentProfiles is Array values
            ? values.Cast<object>().Select(value => Convert.ToInt32(value)).ToArray()
            : [];
        if (!IsWritableMedia(mediaKind, Convert.ToInt32(format.CurrentPhysicalMediaType), profiles))
        {
            string medium = mediaKind switch
            {
                OpticalBurnMediaKind.CdAudio or OpticalBurnMediaKind.CdData => "CD-R or CD-RW",
                OpticalBurnMediaKind.DvdData => "DVD",
                OpticalBurnMediaKind.BluRayData => "unformatted single-layer sequential BD-R (no spare area)",
                _ => throw new ArgumentOutOfRangeException(nameof(mediaKind)),
            };
            throw new InvalidOperationException($"The selected burn mode requires a writable {medium} disc; the current disc is not compatible.");
        }
    }

    internal static bool IsWritableMedia(OpticalBurnMediaKind mediaKind, int physicalMediaType,
        IReadOnlyCollection<int> currentProfiles)
    {
        ArgumentNullException.ThrowIfNull(currentProfiles);
        // Pair the physical disc type with the loaded medium's current profile.
        // IMAPI's data format also accepts CDs and BDs, so its generic support
        // check alone must not determine whether a DVD drawing can be written.
        return mediaKind switch
        {
            OpticalBurnMediaKind.CdAudio or OpticalBurnMediaKind.CdData => physicalMediaType switch
            {
                0x2 => currentProfiles.Contains(0x9), // CD-R
                0x3 => currentProfiles.Contains(0xA), // CD-RW
                _ => false,
            },
            OpticalBurnMediaKind.DvdData => physicalMediaType switch
            {
                0x5 => currentProfiles.Contains(0x12), // DVD-RAM
                0x6 => currentProfiles.Contains(0x1B), // DVD+R
                0x7 => currentProfiles.Contains(0x1A), // DVD+RW
                0x8 => currentProfiles.Contains(0x2B), // DVD+R DL
                0x9 => currentProfiles.Contains(0x11), // DVD-R
                0xA => currentProfiles.Contains(0x13) || currentProfiles.Contains(0x14), // DVD-RW
                0xB => currentProfiles.Contains(0x15) || currentProfiles.Contains(0x16), // DVD-R DL
                0xD => currentProfiles.Contains(0x2A), // DVD+RW DL
                _ => false,
            },
            OpticalBurnMediaKind.BluRayData => IsWritableBluRayMedia(physicalMediaType, currentProfiles),
            _ => throw new ArgumentOutOfRangeException(nameof(mediaKind)),
        };
    }

    internal static bool IsWritableBluRayMedia(int physicalMediaType, IReadOnlyCollection<int> currentProfiles)
    {
        ArgumentNullException.ThrowIfNull(currentProfiles);
        // Inspect the loaded medium's current profiles, never the drive's supported
        // profiles: a Blu-ray recorder can also contain a CD, DVD or read-only BD.
        return physicalMediaType == BluRayRecordableMedia
            && currentProfiles.Contains(BluRaySequentialProfile);
    }

    internal static void ValidateDataMedia(OpticalBurnRequest request, int mediaState, long freeSectors)
    {
        string mediaName = ValidateWritableDataCapacity(request.MediaKind, mediaState, freeSectors);
        long requiredSectors = request.ContentLength / 2048;
        if (freeSectors < requiredSectors)
        {
            long deficit = requiredSectors - freeSectors;
            throw new InvalidOperationException(
                $"Not enough free space on {mediaName}: {requiredSectors:N0} sectors needed ({requiredSectors / 512.0:F3} MiB), "
                + $"{freeSectors:N0} sectors actually writable ({freeSectors / 512.0:F3} MiB), "
                + $"short by {deficit:N0} sectors ({deficit / 512.0:F3} MiB).");
        }
    }

    internal static string ValidateWritableDataCapacity(OpticalBurnMediaKind mediaKind,
        int mediaState, long freeSectors)
    {
        string mediaName = mediaKind switch
        {
            OpticalBurnMediaKind.CdData => "CD",
            OpticalBurnMediaKind.DvdData => "DVD",
            OpticalBurnMediaKind.BluRayData => "Blu-ray disc",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaKind), "Expected CD, DVD or Blu-ray data."),
        };
        if ((mediaState & BlankMediaState) == 0)
        {
            throw new InvalidOperationException($"{mediaName} is not blank; the burn was canceled.");
        }

        if (freeSectors <= 0)
        {
            throw new InvalidOperationException($"{mediaName} did not report usable data capacity ({freeSectors:N0} sectors).");
        }
        return mediaName;
    }

    private static void ReportWriteSpeed(
        IProgress<OpticalBurnProgress>? progress,
        OpticalWriteSpeed speed,
        OpticalBurnRequest request)
    {
        progress?.Report(new OpticalBurnProgress(
            "speed",
            $"Device set speed={speed.GetMultiplier(request.MediaKind):0.#}×"
                + $" · {speed.GetMegabytesPerSecond(request.MediaKind):0.0} MB/s"
                + (speed.RotationTypeIsPureCav ? " · CAV" : "")
                + " (value returned by the device; real-time throughput is measured separately).",
            0,
            request.ContentLength));
    }

    private static IReadOnlyList<OpticalWriteSpeed> ReadSupportedWriteSpeeds(dynamic format)
    {
        List<OpticalWriteSpeed> result = [];
        try
        {
            if (format.SupportedWriteSpeedDescriptors is Array descriptors)
            {
                foreach (object? item in descriptors)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        dynamic descriptor = item;
                        int sectorsPerSecond = Convert.ToInt32(descriptor.WriteSpeed);
                        if (sectorsPerSecond > 0)
                        {
                            result.Add(new OpticalWriteSpeed(
                                sectorsPerSecond,
                                Convert.ToBoolean(descriptor.RotationTypeIsPureCAV)));
                        }
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }
            }
        }
        catch (COMException)
        {
            // Older drives may expose only the simple speed list. Fall back below.
        }

        if (result.Count == 0 && format.SupportedWriteSpeeds is Array speeds)
        {
            foreach (object? item in speeds)
            {
                int sectorsPerSecond = Convert.ToInt32(item);
                if (sectorsPerSecond > 0)
                {
                    result.Add(new OpticalWriteSpeed(sectorsPerSecond, false));
                }
            }
        }

        return result
            .Distinct()
            .OrderBy(speed => speed.SectorsPerSecond)
            .ThenBy(speed => speed.RotationTypeIsPureCav)
            .ToArray();
    }

    internal static OpticalWriteSpeed? ResolveWriteSpeed(
        OpticalWriteSpeed? requested,
        IReadOnlyList<OpticalWriteSpeed> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        return requested ?? supported
            .OrderBy(speed => speed.SectorsPerSecond)
            .ThenBy(speed => speed.RotationTypeIsPureCav)
            .FirstOrDefault();
    }

    private static void TryCancel(dynamic format, bool cdAudio)
    {
        try
        {
            if (cdAudio)
            {
                format.CancelAddTrack();
            }
            else
            {
                format.CancelWrite();
            }
        }
        catch
        {
            // The operation may already be finishing; the producer token is also cancelled.
        }
    }

    private static dynamic CreateRecorder(string id)
    {
        dynamic recorder = CreateComObject("IMAPI2.MsftDiscRecorder2");
        recorder.InitializeDiscRecorder(id);
        return recorder;
    }

    private static dynamic CreateComObject(string programmaticId)
    {
        Type type = Type.GetTypeFromProgID(programmaticId, throwOnError: false)
            ?? throw new PlatformNotSupportedException(
                "Windows Image Mastering API 2 (IMAPI2) is not available.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Unable to create Windows burning component {programmaticId}.");
    }

    private static IReadOnlyList<string> ReadStrings(object? value)
    {
        if (value is not Array array)
        {
            return [];
        }

        List<string> result = [];
        foreach (object? item in array)
        {
            string? text = Convert.ToString(item)?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static void ReleaseComObject(object value)
    {
        if (Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static Task<T> RunOnStaThread<T>(Func<T> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "DiscImageStudio.IMAPI2",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
