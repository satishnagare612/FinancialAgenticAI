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

    # if preset_name == "gemini":
    #     # Use the native Gemini client for best structured-output support.
    #     # This requires: pip install langchain-google-genai
    #     try:
    #         from langchain_google_genai import ChatGoogleGenerativeAI
    #     except ImportError as e:
    #         raise ImportError(
    #             "The 'langchain-google-genai' package is required for the "
    #             "Gemini provider. Install it with: pip install langchain-google-genai"
    #         ) from e

    #     llm = ChatGoogleGenerativeAI(
    #         model=model,
    #         google_api_key=SecretStr(api_key),
    #         temperature=temperature,
    #         timeout=timeout_seconds,
    #         max_retries=max_retries,
    #     )
    #     # The native client supports method="json_schema" for strict output.
    #     return _StructuredAdapter(llm, structured_method)

    # Grok, OpenRouter, and any other OpenAI-compatible provider
    if structured_method not in ("json_schema", "function_calling", "json_mode"):
        raise ValueError(
            f"Unsupported structured_method '{structured_method}'. "
            "Use json_schema, function_calling, or json_mode."
        )

    from langchain_openai import ChatOpenAI

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
