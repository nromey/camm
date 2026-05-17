namespace Camm.Speech;

// Options parsed from a speech-marker line by the consumer's
// IScreenReaderMarkerProtocol. Init-only record so consumers can
// extend their own implementations without breaking CAMM callers.
public record SpeechOptions(bool NoInterrupt = false);
