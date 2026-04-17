import { extractPipelineNodes } from '../src/utils/pipeline_utils';

describe('Greedy Pipeline Extraction', () => {
  test('should extract from standard lowercase "roots"', () => {
    const messages = [{ type: 'progress', data: { roots: [{ id: '1' }] } }];
    expect(extractPipelineNodes(messages)).toHaveLength(1);
  });

  test('should extract from uppercase "Roots"', () => {
    const messages = [{ type: 'progress', data: { Roots: [{ id: '2' }] } }];
    expect(extractPipelineNodes(messages)).toHaveLength(1);
  });

  test('should extract from arbitrary key name (Greedy Search)', () => {
    const messages = [{ type: 'progress', data: { some_weird_key: [{ id: '3' }] } }];
    expect(extractPipelineNodes(messages)).toHaveLength(1);
  });

  test('should extract if data itself is the array', () => {
    const messages = [{ type: 'progress', data: [{ id: '4' }] }];
    expect(extractPipelineNodes(messages)).toHaveLength(1);
  });

  test('should return empty array if no progress messages exist', () => {
    const messages = [{ type: 'status', status: 'ready' }];
    expect(extractPipelineNodes(messages as any)).toHaveLength(0);
  });

  test('should return empty if data has no arrays', () => {
    const messages = [{ type: 'progress', data: { foo: 'bar', age: 42 } }];
    expect(extractPipelineNodes(messages as any)).toHaveLength(0);
  });
});
