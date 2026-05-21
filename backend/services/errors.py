from __future__ import annotations


class PipelineError(Exception):
    """Expected client-facing error from request validation or providers."""

    def __init__(self, message: str, status_code: int = 400, code: str = "pipeline_error"):
        super().__init__(message)
        self.message = message
        self.status_code = status_code
        self.code = code
