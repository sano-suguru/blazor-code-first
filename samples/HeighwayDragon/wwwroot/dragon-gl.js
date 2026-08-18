let gl = null;
let canvas = null;
let program = null;
let positionBuffer = null;
let matrixLoc = null;
let totalVerticesLoc = null;

let totalVertices = 0;
let bounds = { minX: 0, maxX: 0, minY: 0, maxY: 0 };
let panX = 0;
let panY = 0;
let zoom = 1;
let renderRequested = false;
let scratchBytes = new Uint8Array(0);

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 a_position;
uniform mat3 u_matrix;
uniform float u_totalVertices;
out float v_progress;

void main() {
    vec3 pos = u_matrix * vec3(a_position, 1.0);
    gl_Position = vec4(pos.xy, 0.0, 1.0);
    v_progress = float(gl_VertexID) / u_totalVertices;
}`;

const FRAGMENT_SHADER = `#version 300 es
precision highp float;
in float v_progress;
out vec4 outColor;

void main() {
    vec3 col = 0.5 + 0.5 * cos(6.28318 * (v_progress + vec3(0.0, 0.33, 0.67)));
    outColor = vec4(col, 1.0);
}`;

function compileShader(type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(shader);
        gl.deleteShader(shader);
        throw new Error(`dragon-gl: shader compile failed: ${log}`);
    }
    return shader;
}

export function initGl(canvasElement) {
    canvas = canvasElement;
    gl = canvas.getContext("webgl2");
    if (!gl) {
        return false;
    }

    program = gl.createProgram();
    gl.attachShader(program, compileShader(gl.VERTEX_SHADER, VERTEX_SHADER));
    gl.attachShader(program, compileShader(gl.FRAGMENT_SHADER, FRAGMENT_SHADER));
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error(`dragon-gl: program link failed: ${gl.getProgramInfoLog(program)}`);
    }
    gl.useProgram(program);

    const positionLoc = gl.getAttribLocation(program, "a_position");
    matrixLoc = gl.getUniformLocation(program, "u_matrix");
    totalVerticesLoc = gl.getUniformLocation(program, "u_totalVertices");

    positionBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.enableVertexAttribArray(positionLoc);
    gl.vertexAttribPointer(positionLoc, 2, gl.FLOAT, false, 0, 0);

    window.addEventListener("resize", requestRender);
    return true;
}

export function uploadPoints(pointBytesView, vertexCount, minX, maxX, minY, maxY) {
    // pointBytesView is the Point[] array reinterpreted as bytes on the C# side
    // (MemoryMarshal.AsBytes) -- the JSImport marshaller only supports MemoryView over byte
    // spans, not float spans directly. Reinterpret the bytes back into a Float32Array view over
    // the same buffer, so this costs no extra copy beyond the one MemoryView already requires.
    const byteLength = vertexCount * 2 * 4;
    if (scratchBytes.length < byteLength) {
        scratchBytes = new Uint8Array(byteLength);
    }
    const bytes = scratchBytes.subarray(0, byteLength);
    pointBytesView.copyTo(bytes);
    const floats = new Float32Array(bytes.buffer, bytes.byteOffset, vertexCount * 2);

    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, floats, gl.STATIC_DRAW);

    bounds = { minX, maxX, minY, maxY };
    totalVertices = vertexCount;
    panX = 0;
    panY = 0;
    zoom = 1;
    requestRender();
}

export function pan(dxPixels, dyPixels) {
    const { scaleX, scaleY } = currentScale();
    panX += (dxPixels / canvas.width * 2) / (scaleX * zoom);
    panY -= (dyPixels / canvas.height * 2) / (scaleY * zoom);
    requestRender();
}

const MIN_ZOOM = 0.05;
const MAX_ZOOM = 200;

export function zoomBy(factor) {
    zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, zoom * factor));
    requestRender();
}

export function capturePointer(pointerId) {
    canvas.setPointerCapture(pointerId);
}

function currentScale() {
    const width = bounds.maxX - bounds.minX;
    const height = bounds.maxY - bounds.minY;
    const aspect = canvas.width / canvas.height;
    const padding = Math.max(width, height) * 0.05;
    const viewW = width + padding * 2;
    const viewH = height + padding * 2;

    let scaleX = 2.0 / viewW;
    let scaleY = 2.0 / viewH;
    if (viewW / viewH > aspect) {
        scaleY = scaleX * aspect;
    } else {
        scaleX = scaleY / aspect;
    }
    return { scaleX, scaleY };
}

function requestRender() {
    if (renderRequested || !gl || totalVertices === 0) {
        return;
    }
    renderRequested = true;
    requestAnimationFrame(render);
}

function render() {
    renderRequested = false;

    const desiredWidth = window.innerWidth * window.devicePixelRatio;
    const desiredHeight = window.innerHeight * window.devicePixelRatio;
    if (canvas.width !== desiredWidth || canvas.height !== desiredHeight) {
        canvas.width = desiredWidth;
        canvas.height = desiredHeight;
        gl.viewport(0, 0, canvas.width, canvas.height);
    }

    const { scaleX: baseScaleX, scaleY: baseScaleY } = currentScale();
    const scaleX = baseScaleX * zoom;
    const scaleY = baseScaleY * zoom;
    const centerX = (bounds.minX + bounds.maxX) / 2;
    const centerY = (bounds.minY + bounds.maxY) / 2;

    const matrix = new Float32Array([
        scaleX, 0.0, 0.0,
        0.0, scaleY, 0.0,
        -(centerX - panX) * scaleX, -(centerY - panY) * scaleY, 1.0
    ]);

    gl.uniformMatrix3fv(matrixLoc, false, matrix);
    gl.uniform1f(totalVerticesLoc, totalVertices);

    gl.clearColor(0.07, 0.07, 0.07, 1.0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawArrays(gl.LINE_STRIP, 0, totalVertices);
}
