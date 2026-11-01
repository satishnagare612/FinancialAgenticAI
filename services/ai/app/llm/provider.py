"""OpenAI-compatible provider factory (LLM_PROVIDER=real)."""

from typing import Any, Protocol

from pydantic import BaseModel, SecretStr


class StructuredLlm(Protocol):
    """Narrow contract used by build_graph.

    Both StubLlm and the adapter below satisfy this: with_structured_output
    returns an object with an async .ainvoke(messages) -> schema instance.
    """

    def with_structured_output(self, schema: type[BaseModel]) -> Any: ...


class _StructuredAdapter:
    """Wraps a ChatOpenAI instance so with_structured_output honours the
    provider's declared structured-output method."""

    def __init__(self, llm: Any, structured_method: str) -> None:
        self._llm = llm
        self._method = structured_method

    def with_structured_output(self, schema: type[BaseModel]) -> Any:
        return self._llm.with_structured_output(schema, method=self._method)


def build_real_llm(
    preset_name: str,
    base_url: str,
    api_key: str,
    model: str,
    structured_method: str = "function_calling",
    temperature: float = 0.0,
    timeout_seconds: float = 60.0,
    max_retries: int = 2,
) -> StructuredLlm:
    if not api_key:
        raise ValueError(
            f"LLM_API_KEY is required for provider '{preset_name}'. "
            "Set it via environment variable or .env file."
        )

    # Grok, OpenRouter, and any other OpenAI-compatible provider
    if structured_method not in ("json_schema", "function_calling", "json_mode"):
        raise ValueError(
            f"Unsupported structured_method '{structured_method}'. "
            "Use json_schema, function_calling, or json_mode."
        )

    from langchain_openai import ChatOpenAI

    # For json_mode providers (Ollama, others that ignore LangChain's
    # prompt-only JSON instruction), explicitly force response_format on
    # the wire so the model is constrained to emit JSON. Without this,
    # ChatOpenAI.with_structured_output(method="json_mode") may only
    # prepend a prompt hint — which models like Gemma 4 on Ollama Cloud
    # ignore, producing prose that fails JSON parsing.
    if structured_method == "json_mode":
        llm = ChatOpenAI(
            model=model,
            api_key=SecretStr(api_key),
            base_url=base_url,
            temperature=temperature,
            timeout=timeout_seconds,
            max_retries=max_retries,
            default_headers={"HTTP-Referer": "https://financialai.local"},
            model_kwargs={"response_format": {"type": "json_object"}},
        )
    else:
        llm = ChatOpenAI(
            model=model,
            api_key=SecretStr(api_key),
            base_url=base_url,
            temperature=temperature,
            timeout=timeout_seconds,
            max_retries=max_retries,
            default_headers={"HTTP-Referer": "https://financialai.local"},
        )
    return _StructuredAdapter(llm, structured_method)
