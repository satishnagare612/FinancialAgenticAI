from pydantic_settings import BaseSettings, SettingsConfigDict

# Per-provider defaults. Setting LLM_PROVIDER=groq resolves base_url and model
# from this table. LLM_BASE_URL and LLM_MODEL override independently if set.
#
# structured_method is the method passed to ChatOpenAI.with_structured_output:
#   json_schema       — strictest, OpenAI/Azure/Gemini only
#   function_calling  — tool-based, works on Groq, OpenRouter, Mistral, most
#   json_mode         — response_format=json_object, works on Ollama, some HF
PROVIDER_PRESETS: dict[str, dict[str, str]] = {
    "openrouter": {
        "base_url": "https://openrouter.ai/api/v1",
        "model": "deepseek/deepseek-r1-0528:free",
        "structured_method": "function_calling",
    },
    "groq": {
        "base_url": "https://api.groq.com/openai/v1",
        "model": "openai/gpt-oss-120b",
        "structured_method": "function_calling",
    },
    "gemini": {
        "base_url": "https://generativelanguage.googleapis.com/v1beta/openai/",
        "model": "gemini-3.8-flash",
        "structured_method": "json_schema",
    },
    "cerebras": {
        "base_url": "https://api.cerebras.ai/v1",
        "model": "gpt-oss-120b",
        "structured_method": "function_calling",
    },
    "mistral": {
        "base_url": "https://api.mistral.ai/v1",
        "model": "mistral-large-3",
        "structured_method": "function_calling",
    },
    "github": {
        "base_url": "https://models.github.ai/inference",
        "model": "openai/gpt-4.1",
        "structured_method": "function_calling",
    },
    "ollama": {
        "base_url": "http://localhost:11434/v1",
        "model": "llama3.3",
        "structured_method": "json_mode",
    },
}


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    # stub — deterministic test double (gated suite + default dev)
    # real — OpenAI-compatible endpoint; base_url/model resolved from preset or overrides
    llm_provider: str = "stub"

    # Name of the provider preset to use when llm_provider=real.
    # Set to a key of PROVIDER_PRESETS (openrouter, groq, gemini, ...).
    # Empty string means "use llm_base_url + llm_model verbatim, no preset".
    llm_preset: str = "openrouter"

    # Explicit overrides; empty string means "use preset value".
    llm_base_url: str = ""
    llm_api_key: str = ""
    llm_model: str = ""

    llm_temperature: float = 0.0
    llm_timeout_seconds: float = 60.0
    llm_max_retries: int = 2


settings = Settings()


def resolve_llm_config() -> tuple[str, str, str]:
    """Return (base_url, model, structured_method) for the active configuration.

    Preset supplies defaults; explicit env vars override individual fields.
    Raises ValueError if the preset name is unknown or required fields are empty.
    """
    preset_name = settings.llm_preset or ""
    preset = PROVIDER_PRESETS.get(preset_name, {})

    base_url = settings.llm_base_url or preset.get("base_url", "")
    model = settings.llm_model or preset.get("model", "")
    method = preset.get("structured_method", "function_calling")

    if not base_url:
        raise ValueError(
            f"No base_url: set LLM_BASE_URL or pick a known preset "
            f"(LLM_PRESET). Known presets: {sorted(PROVIDER_PRESETS)}"
        )
    if not model:
        raise ValueError(
            "No model: set LLM_MODEL or pick a known preset (LLM_PRESET)."
        )

    return base_url, model, method
